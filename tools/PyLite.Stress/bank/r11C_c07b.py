import typing
print(typing.get_args(typing.Union[int, str]), typing.get_args(typing.List[int]), typing.get_origin(typing.List[int]), typing.get_args(int | str), typing.get_args(typing.Dict[str, int]), typing.get_args(typing.Literal[1, 2]), typing.get_origin(typing.Union[int, str]))
