using System;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    internal struct TableEntry
    {
        public long Hash;
        public ScriptValue Key;      // null => tombstone
        public ScriptValue Value;    // null for set usage
    }

    // Insertion-ordered open-addressing table: dense entries array (insertion order) + sparse index.
    // Port of the CPython 3.6 compact-dict design. Key comparison may reenter script code (a record
    // __eq__); it goes through PyOps.Equals with a threaded depth.
    internal sealed class OrderedTable
    {
        private const int EMPTY = -1;
        private const int DUMMY = -2;

        private TableEntry[] _entries;   // dense, insertion order
        private int _entriesUsed;        // live + tombstones
        private int _liveCount;
        private int[] _index;            // power of two, >= 8
        private int _usedSlots;          // live + DUMMY
        private ulong _version;
        private int _head;               // no live entry below it: FindFirstLive resumes here

        private OrderedTable() { }   // structural-clone ctor: the caller fills every field

        public OrderedTable(EvalContext ctx, int capacityHint)
        {
            int indexSize, entriesCap;
            if (capacityHint <= 5)
            {
                indexSize = 8;
                entriesCap = 5;
            }
            else
            {
                // CPython's presize: room for exactly capacityHint keys, the index at most 2/3 full
                indexSize = NextPow2(capacityHint + capacityHint / 2 + 2);
                entriesCap = capacityHint;
            }
            ctx.Budget.ChargeAllocation(4L * indexSize + 24L * entriesCap);
            _index = NewIndex(indexSize);
            _entries = new TableEntry[entriesCap];
        }

        public int Count { get { return _liveCount; } }
        public ulong Version { get { return _version; } }
        public int EntriesUsed { get { return _entriesUsed; } }

        private static int[] NewIndex(int size)
        {
            var a = new int[size];
            for (int i = 0; i < size; i++)
                a[i] = EMPTY;
            return a;
        }

        private static int NextPow2(int n) { int p = 8; while (p < n)
            p <<= 1; return p; }

        // Exact port of the dictobject.c lookdict probe (perturb >>= 5; i = i*5 + perturb + 1).
        private int FindSlot(ScriptValue key, long hash, EvalContext ctx, int depth)
        {
            ulong mask = (ulong)(_index.Length - 1);
            ulong i = (ulong)hash & mask;
            ulong perturb = (ulong)hash;
            int probes = 0;
            while (true)
            {
                int slot = _index[(int)i];
                if (slot == EMPTY)
                    return -1;
                if (slot != DUMMY)
                {
                    TableEntry e = _entries[slot];
                    if (e.Hash == hash && (ReferenceEquals(e.Key, key) || PyOps.Equals(e.Key, key, ctx, depth + 1)))
                        return slot;
                }

                perturb >>= 5;
                i = (i * 5 + perturb + 1) & mask;
                probes++;
                if (probes > 8)
                    ctx.Budget.Step();
            }
        }

        public bool TryGetValue(ScriptValue key, long hash, EvalContext ctx, int depth, out ScriptValue value)
        {
            int pos;
            return TryGetValueWithPos(key, hash, ctx, depth, out value, out pos);
        }

        // Positional access for the global-name cache: an entry position stays valid (and its Key
        // non-null) exactly while Version is unchanged — inserts of NEW keys, deletes, clears and
        // rebuilds all bump it; value updates neither bump nor move.
        public bool TryGetValueWithPos(ScriptValue key, long hash, EvalContext ctx, int depth,
            out ScriptValue value, out int pos)
        {
            pos = FindSlot(key, hash, ctx, depth);
            if (pos >= 0)
            {
                value = _entries[pos].Value;
                return true;
            }
            value = null;
            return false;
        }

        public ScriptValue ValueAt(int pos) { return _entries[pos].Value; }

        public void SetValueAt(int pos, ScriptValue value) { _entries[pos].Value = value; }

        public bool ContainsKey(ScriptValue key, long hash, EvalContext ctx, int depth)
        {
            return FindSlot(key, hash, ctx, depth) >= 0;
        }

        // True = a NEW key was inserted (version bumped); false = in-place update (R6: version and
        // positions unchanged).
        public bool InsertOrUpdate(ScriptValue key, long hash, ScriptValue value, EvalContext ctx, int depth)
        {
            int pos;
            return InsertOrUpdateWithPos(key, hash, value, ctx, depth, out pos);
        }

        // Same op, reporting the entry position (valid under the post-op Version) so a global write
        // can prime the name cache — write-only loop variables then hit SetValueAt from the second
        // iteration on.
        public bool InsertOrUpdateWithPos(ScriptValue key, long hash, ScriptValue value, EvalContext ctx,
            int depth, out int pos)
        {
            int free;
            ulong before = _version;   // a script __eq__ called by the lookup may have changed the table
            int slot = FindSlotOrFree(key, hash, ctx, depth, out free);
            if (slot >= 0)
            {
                _entries[slot].Value = value;
                pos = slot;
                return false;
            }   // update: version unchanged (R6)
            if (_version == before && _liveCount < ctx.Limits.MaxCollectionItems
                && (_usedSlots + 1) * 3 < _index.Length * 2 && _entriesUsed < _entries.Length)
            {
                Place(free, key, hash, value, ctx);   // the lookup's probe already ended where the key goes
            }
            else
            {
                EnsureCapacityForInsert(ctx);
                InsertNewKey(key, hash, value, ctx);
            }
            pos = _entriesUsed - 1;   // both append
            return true;
        }

        // FindSlot that also reports, on a miss, the index position InsertNewKey would pick: the first
        // DUMMY on the probe path, else the EMPTY that ended it.
        private int FindSlotOrFree(ScriptValue key, long hash, EvalContext ctx, int depth, out int free)
        {
            ulong mask = (ulong)(_index.Length - 1);
            ulong i = (ulong)hash & mask;
            ulong perturb = (ulong)hash;
            int probes = 0;
            int dummy = -1;
            while (true)
            {
                int slot = _index[(int)i];
                if (slot == EMPTY)
                {
                    free = dummy >= 0 ? dummy : (int)i;
                    return -1;
                }
                if (slot != DUMMY)
                {
                    TableEntry e = _entries[slot];
                    if (e.Hash == hash && (ReferenceEquals(e.Key, key) || PyOps.Equals(e.Key, key, ctx, depth + 1)))
                    {
                        free = -1;
                        return slot;
                    }
                }
                else if (dummy < 0)
                {
                    dummy = (int)i;
                }

                perturb >>= 5;
                i = (i * 5 + perturb + 1) & mask;
                probes++;
                if (probes > 8)
                    ctx.Budget.Step();
            }
        }

        private void Place(int target, ScriptValue key, long hash, ScriptValue value, EvalContext ctx)
        {
            ctx.Budget.ChargeAllocation(48); // per live entry (R5)
            _entries[_entriesUsed] = new TableEntry
            {
                Hash = hash,
                Key = key,
                Value = value
            };
            if (_index[target] == EMPTY)
                _usedSlots++; // consumed an EMPTY slot
            _index[target] = _entriesUsed;
            _entriesUsed++;
            _liveCount++;
            _version++;
        }

        private void EnsureCapacityForInsert(EvalContext ctx)
        {
            if (_liveCount >= ctx.Limits.MaxCollectionItems)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxCollectionItems", ctx.Limits.MaxCollectionItems, _liveCount + 1L);
            if ((_usedSlots + 1) * 3 >= _index.Length * 2)
            {
                Resize(ctx);
                return;
            }
            if (_entriesUsed == _entries.Length)
            {
                if (_liveCount * 2 <= _entriesUsed)
                    Compact(ctx);
                else
                    GrowEntries(ctx);
            }
        }

        private void InsertNewKey(ScriptValue key, long hash, ScriptValue value, EvalContext ctx)
        {
            ulong mask = (ulong)(_index.Length - 1);
            ulong i = (ulong)hash & mask;
            ulong perturb = (ulong)hash;
            int probes = 0;
            int freeslot = -1;
            while (true)
            {
                int slot = _index[(int)i];
                if (slot == EMPTY)
                {
                    Place(freeslot >= 0 ? freeslot : (int)i, key, hash, value, ctx);
                    return;
                }

                if (slot == DUMMY)
                {
                    if (freeslot < 0)
                        freeslot = (int)i;
                }

                perturb >>= 5;
                i = (i * 5 + perturb + 1) & mask;
                probes++;
                if (probes > 8)
                    ctx.Budget.Step();
            }
        }

        public bool Delete(ScriptValue key, long hash, EvalContext ctx, int depth)
        {
            ulong mask = (ulong)(_index.Length - 1);
            ulong i = (ulong)hash & mask;
            ulong perturb = (ulong)hash;
            int probes = 0;
            while (true)
            {
                int slot = _index[(int)i];
                if (slot == EMPTY)
                    return false;
                if (slot != DUMMY)
                {
                    TableEntry e = _entries[slot];
                    if (e.Hash == hash && (ReferenceEquals(e.Key, key) || PyOps.Equals(e.Key, key, ctx, depth + 1)))
                    {
                        _index[(int)i] = DUMMY;
                        _entries[slot].Key = null;
                        _entries[slot].Value = null;
                        _liveCount--;
                        _version++;
                        // Trailing tombstones are dropped (as CPython's popitem does), so draining from
                        // the end never rescans them.
                        if (slot == _entriesUsed - 1)
                        {
                            while (_entriesUsed > 0 && _entries[_entriesUsed - 1].Key == null)
                                _entriesUsed--;
                            if (_head > _entriesUsed)
                                _head = _entriesUsed;
                        }
                        return true;
                    }
                }

                perturb >>= 5;
                i = (i * 5 + perturb + 1) & mask;
                probes++;
                if (probes > 8)
                    ctx.Budget.Step();
            }
        }

        public void Clear(EvalContext ctx)
        {
            ctx.Budget.ChargeAllocation(4L * 8 + 24L * 5);
            _index = NewIndex(8);
            _entries = new TableEntry[5];
            _entriesUsed = 0;
            _liveCount = 0;
            _usedSlots = 0;
            _head = 0;
            _version++;
        }

        public bool TryGetEntryAt(int pos, out long hash, out ScriptValue key, out ScriptValue value)
        {
            TableEntry e = _entries[pos];
            if (e.Key == null)
            {
                hash = 0;
                key = null;
                value = null;
                return false;
            }
            hash = e.Hash; key = e.Key; value = e.Value;
            return true;
        }

        // Constant time after a trailing delete: Delete trims the tombstones at the end.
        public int FindLastLive()
        {
            for (int p = _entriesUsed - 1; p >= 0; p--)
                if (_entries[p].Key != null)
                    return p;
            return -1;
        }

        // OrderedDict.move_to_end(key, last=False): the entry moves to position 0 and the ones before it shift
        // up by one. Linear array moves and one pass over the index, but no rehash and no allocation (it used
        // to rebuild the whole table). False when the key is absent.
        public bool MoveToFront(ScriptValue key, long hash, EvalContext ctx, int depth)
        {
            int p = FindSlot(key, hash, ctx, depth);
            if (p < 0)
                return false;
            ctx.Budget.ChargeLinear((long)p + _index.Length);
            TableEntry e = _entries[p];
            Array.Copy(_entries, 0, _entries, 1, p);
            _entries[0] = e;
            for (int k = 0; k < _index.Length; k++)
            {
                int slot = _index[k];
                if (slot == p)
                    _index[k] = 0;
                else if (slot >= 0 && slot < p)
                    _index[k] = slot + 1;
            }
            _head = 0;
            _version++;
            return true;
        }

        // Amortised constant time while draining from the front (set.pop, OrderedDict.popitem(False)).
        public int FindFirstLive()
        {
            while (_head < _entriesUsed && _entries[_head].Key == null)
                _head++;
            return _head < _entriesUsed ? _head : -1;
        }

        public OrderedTable CloneShallow(EvalContext ctx)
        {
            // Structural copy: identical hashes => identical index layout, so both arrays copy verbatim
            // instead of re-probing and re-inserting every entry (dict/set copy and the set operators
            // hit this on every call). Tombstones are preserved; the next resize compacts as usual.
            ctx.Budget.ChargeAllocation(4L * _index.Length + 24L * _entries.Length);
            var t = new OrderedTable();
            t._index = (int[])_index.Clone();
            t._entries = (TableEntry[])_entries.Clone();
            t._entriesUsed = _entriesUsed;
            t._liveCount = _liveCount;
            t._usedSlots = _usedSlots;
            t._head = _head;
            return t;
        }

        private void Resize(EvalContext ctx)
        {
            int newSize = 8;
            while (newSize < _liveCount * 3)
                newSize <<= 1;
            int newEntriesCap = (newSize * 2) / 3;
            ctx.Budget.ChargeAllocation(4L * newSize + 24L * newEntriesCap);
            if (_liveCount > 100_000)
                ctx.Budget.CheckDeadlineNow();
            RebuildInto(newSize, newEntriesCap);
            if (_liveCount > 100_000)
                ctx.Budget.CheckDeadlineNow();
        }

        private void Compact(EvalContext ctx)
        {
            ctx.Budget.ChargeAllocation(4L * _index.Length + 24L * _entries.Length);
            RebuildInto(_index.Length, _entries.Length);
        }

        private void RebuildInto(int indexSize, int entriesCap)
        {
            var newEntries = new TableEntry[entriesCap];
            int w = 0;
            for (int r = 0; r < _entriesUsed; r++)
                if (_entries[r].Key != null)
                    newEntries[w++] = _entries[r];
            _entries = newEntries;
            _entriesUsed = w;
            _head = 0;
            _index = NewIndex(indexSize);
            for (int k = 0; k < _entriesUsed; k++)
                ReinsertIndex(k, _entries[k].Hash);
            _usedSlots = _liveCount;
            _version++;
        }

        private void ReinsertIndex(int entryIdx, long hash)
        {
            ulong mask = (ulong)(_index.Length - 1);
            ulong i = (ulong)hash & mask;
            ulong perturb = (ulong)hash;
            while (_index[(int)i] != EMPTY)
            {
                perturb >>= 5;
                i = (i * 5 + perturb + 1) & mask;
            }
            _index[(int)i] = entryIdx;
        }

        private void GrowEntries(EvalContext ctx)
        {
            int newCap = _entries.Length * 2;
            ctx.Budget.ChargeAllocation(24L * newCap);
            var na = new TableEntry[newCap];
            Array.Copy(_entries, na, _entriesUsed);
            _entries = na;
        }
    }
}
