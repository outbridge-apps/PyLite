# deadline: 20000
import typing, copy, functools, itertools
def t(name, f):
    try:
        print(name + '=' + repr(f())[:100])
    except Exception as e:
        print(name + '!' + type(e).__name__ + ':' + str(e)[:70])
L = typing.Literal
t('u2', lambda: typing.get_args(typing.Union[L[1], L[2]]))
t('u2t', lambda: typing.get_args(typing.Union[(L[1], L[2])]))
t('u3', lambda: typing.get_args(typing.Union[int, L[2], str]))
t('u_many', lambda: len(typing.get_args(typing.Union[tuple(L[i] for i in range(3000))])))
t('u_many_t', lambda: len(typing.get_args(typing.Union[tuple(typing.List[L[i]] for i in range(3000))])))
t('batched31', lambda: list(itertools.batched('ab', 2 ** 31)))
t('batched30', lambda: list(itertools.batched('ab', 2 ** 31 - 1)))
N = 50000
a = int
b = int
for i in range(N):
    a = typing.List[a]
    b = typing.List[b]
print('built')
t('eq', lambda: a == b)
t('hash', lambda: hash(a) == hash(b))
t('str', lambda: len(str(a)))
t('args', lambda: typing.get_args(a)[0] == typing.get_args(b)[0])
t('union', lambda: typing.Union[a, b] == a)
t('optional', lambda: typing.Optional[a] is not None)
t('or', lambda: (a | int) is not None)
t('in_set', lambda: len({a, b}))
t('dict_key', lambda: {a: 1}[b])
t('deepcopy', lambda: copy.deepcopy(a) == a)
t('copy', lambda: copy.copy(a) == a)
t('isinst', lambda: isinstance([], a))
t('origin', lambda: typing.get_origin(a))
t('list_eq', lambda: [a] == [b])
t('lit', lambda: typing.Literal[a] == typing.Literal[b])
t('annot', lambda: typing.Annotated[a, 1] == typing.Annotated[b, 1])
t('callable', lambda: typing.Callable[[a], a] == typing.Callable[[b], b])
t('dict_t', lambda: typing.Dict[a, a] == typing.Dict[b, b])
t('tuple_t', lambda: typing.Tuple[a, ...] == typing.Tuple[b, ...])
u = int
v = int
for i in range(N):
    u = typing.Optional[typing.List[u]]
    v = typing.Optional[typing.List[v]]
print('built2')
t('eq2', lambda: u == v)
t('hash2', lambda: hash(u) == hash(v))
t('union2', lambda: typing.Union[u, v, int] == u)
l = int
for i in range(N):
    l = typing.Literal[l]
print('built3')
t('eq3', lambda: l == l)
t('hash3', lambda: hash(l) is not None)
t('union3', lambda: typing.Union[l, int] is not None)
