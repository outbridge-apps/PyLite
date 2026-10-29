# tags: selftest
# json_in.cfg: {"rate": 2, "names": ["a", "b"]}
# out.total: 4
# json_out.echo: {"n":4}
total = cfg["rate"] * len(cfg["names"])
echo = {"n": total}
