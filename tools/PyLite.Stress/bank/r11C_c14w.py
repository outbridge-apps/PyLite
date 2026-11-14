# deadline: 20000
import traceback, csv, io, contextlib, string, textwrap, uuid, copy
def t(name, f):
    try:
        print(name + '=' + repr(f())[:120])
    except Exception as e:
        print(name + '!' + type(e).__name__ + ':' + str(e)[:80])
E = R = Rec = ValueError
def cause_cycle():
    a = ValueError('a')
    b = KeyError('b')
    a.__cause__ = b
    b.__cause__ = a
    try:
        raise a
    except ValueError:
        return len(traceback.format_exc())
t('cause_cycle', cause_cycle)
def context_cycle():
    a = ValueError('a')
    b = KeyError('b')
    a.__context__ = b
    b.__context__ = a
    return len(''.join(traceback.format_exception(a)))
t('context_cycle', context_cycle)
def deep_chain():
    e = ValueError('0')
    for i in range(5000):
        n = ValueError(str(i))
        n.__cause__ = e
        e = n
    return len(''.join(traceback.format_exception(e)))
t('deep_chain', deep_chain)
def deep_context():
    try:
        def f(n):
            try:
                raise ValueError(n)
            except ValueError:
                if n:
                    f(n - 1)
                else:
                    raise
        f(150)
    except ValueError as e:
        return len(traceback.format_exc())
t('deep_context', deep_context)
t('str_raises', lambda: str(E('x')))
t('fmt_str_raises', lambda: ''.join(traceback.format_exception(E('x')))[-60:])
t('repr_exc', lambda: repr(R()))
t('rec_str', lambda: str(Rec()))
t('rec_str_fmt', lambda: ''.join(traceback.format_exception(Rec()))[-60:])
def notes():
    e = ValueError('x')
    for i in range(10000):
        e.add_note('n' * 100)
    return len(''.join(traceback.format_exception(e)))
t('notes_many', notes)
t('note_bad', lambda: ValueError().add_note(5))
def notes_attr():
    e = ValueError('x')
    e.__notes__ = 'notalist'
    return ''.join(traceback.format_exception(e))
t('notes_bad_attr', notes_attr)
def eg():
    g = ExceptionGroup('g', [ValueError(1), ExceptionGroup('h', [KeyError(2), TypeError(3)])])
    return [repr(g.subgroup(ValueError)), repr(g.split(KeyError)), len(g.exceptions), len(''.join(traceback.format_exception(g)))]
t('eg', eg)
def eg_deep():
    g = ValueError('x')
    for i in range(3000):
        g = ExceptionGroup('g', [g])
    return len(''.join(traceback.format_exception(g)))
t('eg_deep', eg_deep)
def eg_deep_split():
    g = ValueError('x')
    for i in range(3000):
        g = ExceptionGroup('g', [g])
    return g.split(ValueError)[0] is not None
t('eg_deep_split', eg_deep_split)
def eg_wide():
    g = ExceptionGroup('g', [ValueError(i) for i in range(100000)])
    return len(''.join(traceback.format_exception(g)))
t('eg_wide', eg_wide)
t('eg_empty', lambda: ExceptionGroup('g', []))
t('eg_bad', lambda: ExceptionGroup('g', [1]))
t('eg_base', lambda: type(BaseExceptionGroup('g', [ValueError()])).__name__)
def exc_args():
    e = ValueError(*range(100000))
    return len(str(e))
t('exc_args_many', exc_args)
def exc_huge_msg_fmt():
    try:
        raise ValueError('x' * 15000000)
    except ValueError:
        return len(traceback.format_exc())
t('exc_huge_msg_fmt', exc_huge_msg_fmt)
def tb_walk():
    try:
        def f(n):
            if n == 0:
                raise ValueError()
            return f(n - 1)
        f(190)
    except ValueError as e:
        return [len(traceback.extract_tb(e.__traceback__)), len(traceback.format_tb(e.__traceback__, limit=-5)), len(traceback.format_exception(e, limit=2 ** 63)), len(traceback.format_exception(e, limit=-(2 ** 63)))]
t('tb_walk', tb_walk)
def tb_stack():
    return len(traceback.format_stack())
t('tb_stack', tb_stack)
def exit_suppress():
    class S:
        def __enter__(s): return s
        def __exit__(s, *a): return 1
    with S():
        raise ValueError()
    return 'suppressed'
t('exit_truthy_suppress', exit_suppress)
def exit_raises():
    class S:
        def __enter__(s): return s
        def __exit__(s, *a): raise KeyError('exit')
    try:
        with S():
            raise ValueError()
    except KeyError as e:
        return repr(e.__context__)
t('exit_raises', exit_raises)
def exit_stack():
    with contextlib.ExitStack() as st:
        for i in range(10000):
            st.callback(lambda: None)
    return 1
t('exitstack_many', exit_stack)
def exit_stack_raise():
    try:
        with contextlib.ExitStack() as st:
            for i in range(100):
                st.callback(lambda i=i: 1 / 0)
    except ZeroDivisionError as e:
        n = 0
        while e is not None and n < 1000:
            e = e.__context__
            n += 1
        return n
t('exitstack_raise_chain', exit_stack_raise)
t('suppress', lambda: [contextlib.suppress(ValueError).__enter__(), 1])
def finally_return():
    try:
        raise ValueError()
    finally:
        return 'swallowed'
t('finally_return', finally_return)
def loop_exc():
    n = 0
    for i in range(200000):
        try:
            raise ValueError(i)
        except ValueError as e:
            n += 1
    return n
t('loop_exc', loop_exc)
t('csv_sniff', lambda: csv.Sniffer().sniff('a;b;c\n1;2;3\n').delimiter)
t('csv_sniff_bad', lambda: csv.Sniffer().sniff(''))
t('csv_sniff_huge', lambda: csv.Sniffer().sniff(('a,' * 5000 + '\n') * 200).delimiter)
t('csv_has_header', lambda: csv.Sniffer().has_header('a,b\n1,2\n'))
t('csv_quote_none', lambda: (lambda b: (csv.writer(b, quoting=csv.QUOTE_NONE, escapechar='\\').writerow(['a,b', 'c"d', '\n']), b.getvalue())[1])(io.StringIO()))
t('csv_quote_none_noesc', lambda: (lambda b: csv.writer(b, quoting=csv.QUOTE_NONE).writerow(['a,b']))(io.StringIO()))
t('csv_dialect_bad', lambda: csv.reader([], delimiter=''))
t('csv_dialect_bad2', lambda: csv.reader([], delimiter='ab'))
t('csv_dialect_same', lambda: list(csv.reader(['a,b'], delimiter=',', quotechar=',')))
t('csv_lineterm', lambda: (lambda b: (csv.writer(b, lineterminator='').writerow([1, 2]), b.getvalue())[1])(io.StringIO()))
t('csv_reader_nul', lambda: list(csv.reader(['a\x00b'])))
t('csv_reader_unterm', lambda: list(csv.reader(['"abc'])))
t('csv_reader_strict', lambda: list(csv.reader(['"a"b'], strict=True)))
t('csv_reader_nonstr', lambda: list(csv.reader([b'a,b'])))
t('csv_reader_nonstr2', lambda: list(csv.reader([1])))
t('csv_field_limit', lambda: [csv.field_size_limit(), csv.field_size_limit(10), list(csv.reader(['a' * 11]))])
t('csv_field_limit_restore', lambda: csv.field_size_limit(131072))
t('csv_dictreader', lambda: list(csv.DictReader(['a,b', '1,2,3', '4'], restkey='r', restval='v')))
t('csv_dictwriter_extra', lambda: (lambda b: csv.DictWriter(b, ['a']).writerow({'a': 1, 'b': 2}))(io.StringIO()))
t('csv_dictwriter_ignore', lambda: (lambda b: (csv.DictWriter(b, ['a'], extrasaction='ignore').writerow({'a': 1, 'b': 2}), b.getvalue())[1])(io.StringIO()))
t('csv_writer_obj', lambda: (lambda b: (csv.writer(b).writerow([None, 1.5, True, float('nan'), 10 ** 30, [1]]), b.getvalue())[1])(io.StringIO()))
t('csv_writer_iter', lambda: (lambda b: (csv.writer(b).writerow(x for x in 'ab'), b.getvalue())[1])(io.StringIO()))
t('csv_quote_strings', lambda: (lambda b: (csv.writer(b, quoting=csv.QUOTE_STRINGS).writerow(['a', 1, None]), b.getvalue())[1])(io.StringIO()))
t('csv_quote_notnull', lambda: (lambda b: (csv.writer(b, quoting=csv.QUOTE_NOTNULL).writerow(['a', 1, None]), b.getvalue())[1])(io.StringIO()))
t('csv_reader_quote_nonnumeric', lambda: list(csv.reader(['1,"a",2.5'], quoting=csv.QUOTE_NONNUMERIC)))
t('csv_reader_quote_nonnumeric_bad', lambda: list(csv.reader(['x'], quoting=csv.QUOTE_NONNUMERIC)))
t('uuid', lambda: [str(uuid.UUID(int=0)), str(uuid.UUID(int=2 ** 128 - 1)), uuid.UUID('{12345678-1234-5678-1234-567812345678}').version, uuid.uuid5(uuid.NAMESPACE_DNS, 'a').hex[:4], uuid.UUID(bytes=b'\x00' * 16).int])
t('uuid_bad', lambda: uuid.UUID(int=2 ** 128))
t('uuid_bad2', lambda: uuid.UUID('zz'))
t('uuid_bad3', lambda: uuid.UUID(bytes=b'\x00' * 15))
t('uuid_cmp', lambda: [uuid.UUID(int=1) < uuid.UUID(int=2), hash(uuid.UUID(int=1)) == hash(uuid.UUID(int=1)), uuid.UUID(int=1) == 1])
t('string_fmt', lambda: string.Formatter().format('{0}{1!r}', 1, 'a'))
t('textwrap_huge_width', lambda: textwrap.wrap('a b ' * 1000, width=2 ** 62)[:1])
t('textwrap_long_word', lambda: len(textwrap.wrap('a' * 100000, width=1)))
t('textwrap_shorten_huge', lambda: textwrap.shorten('a ' * 100000, 10))
t('copy_exc', lambda: [repr(copy.copy(ValueError(1, 2))), repr(copy.deepcopy(KeyError('k')))])
