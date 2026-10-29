# Test report

Written by `tools/run-gates.ps1`; run it to repeat every number below (see `TESTING.md`).

| | |
|---|---|
| PyLite | 1.1.0, commit `7740cbd` |
| run | 2026-10-29 15:40 to 15:47 UTC |
| OS | Microsoft Windows Server 2022 Datacenter Evaluation (10.0.20348) |
| CPU | Intel(R) Xeon(R) CPU E5-2650 v2 @ 2.60GHz, 8 logical processors |
| .NET | Framework 4.8 (release 528449), SDK 9.0.308 |
| reference | IronPython 3.4.2 (NuGet) |
| xUnit | 2.9.2 |

## Gates

| gate | result | outcome | time |
|---|---|---|---:|
| build (PyLite.sln, Release) | pass | warnings as errors in the engine | 28 s |
| xUnit, everyday (Category!=MachineSensitive) | pass | 6797 passed, 0 failed of 6797 | 125 s |
| xUnit, MachineSensitive | pass | 175 passed, 0 failed of 175 | 77 s |
| conformance (CPython Lib/test cases) | pass | 173 passed, 0 failed, 0 skipped | 13 s |
| differential (vs IronPython 3.4.2) | pass | 25 scripts: 24 match, 1 known difference(s), 0 bug(s) | 24 s |
| adversarial (Stress corpus) | pass | 130 probes, the sandbox held on every one | 154 s |

## Differential: PyLite and IronPython 3.4.2 on the same scripts

Each script sets `result`; the two are compared by `repr` (a `dict` order-free, floats by their bits), or by
the exception class when the script raises. A known difference is listed with its reason in
`tools/PyLite.DiffHarness/known-differences.txt`.

| script | class | detail |
|---|---|---|
| float_floordiv_iron.py | Known | value differs (known): (9.0, -1.0, 9.0, -1.0) vs (10.0, -0.0, 9.0, -1.0) |
| arith.py | Match |  |
| bytesio.py | Match |  |
| class_consts.py | Match |  |
| comprehension.py | Match |  |
| contextlib_decimal.py | Match |  |
| dict_order.py | Match |  |
| email_dates.py | Match |  |
| enum_basics.py | Match |  |
| error_div.py | Match |  |
| float_divmod.py | Match |  |
| float_repr.py | Match |  |
| int_big.py | Match |  |
| json_rt.py | Match |  |
| math_fns.py | Match |  |
| sorted_key.py | Match |  |
| str_format.py | Match |  |
| string_template.py | Match |  |
| strings.py | Match |  |
| super_chain.py | Match |  |
| try_else_raise.py | Match |  |
| try_match_error_finally.py | Match |  |
| with_stmt.py | Match |  |
| zipfile_rt.py | Match |  |
| zlib_gzip.py | Match |  |

## Adversarial: which limit stopped each probe

`ok` and `raise` mean the script finished or raised a Python exception it was meant to raise; `budget X`
names the limit that stopped it. A probe that hangs past its deadline or crashes the host would fail the
gate. The probes and their limits are in `tools/PyLite.Stress/bombs`; the groups in its README.

| probe | outcome | ms | steps | MB charged |
|---|---|---:|---:|---:|
| `b1_bytes_upper` | ok | 441 | 1967 | 15 |
| `b2_bytes_split` | ok | 925 | 1967 | 74 |
| `b3_bytes_replace` | ok | 231 | 2002457 | 17 |
| `b4_bytes_title` | ok | 105 | 1967 | 15 |
| `b5_bytes_isalpha` | ok | 124 | 1958 | 7 |
| `c1_encode_cp866` | ok | 313 | 3923 | 22 |
| `c2_decode_cp866` | ok | 259 | 3926 | 22 |
| `c3_encode_utf8` | ok | 81 | 3924 | 30 |
| `c4_decode_utf16` | ok | 175 | 3928 | 15 |
| `c5_encode_replace` | ok | 515 | 3923 | 22 |
| `d1_deep_list_repr` | ok | 264 | 504704 | 10 |
| `d2_deep_list_eq` | ok | 185 | 109514 | 16 |
| `d3_deep_tuple_hash` | ok | 60 | 104827 | 7 |
| `d4_deep_deepcopy` | ok | 238 | 304715 | 16 |
| `d5_deep_json_dumps` | ok | 207 | 104974 | 10 |
| `d6_deep_dataclass_eq` | ok | 432 | 512657 | 9 |
| `d7_deep_dataclass_hash` | ok | 131 | 306411 | 6 |
| `d8_deep_record_repr` | ok | 373 | 810952 | 14 |
| `d9_deep_deque_eq` | ok | 316 | 915784 | 29 |
| `da_deep_deque_repr` | ok | 152 | 507970 | 16 |
| `db_deep_slice_repr` | ok | 81 | 306264 | 7 |
| `dc_deep_partial_repr` | ok | 186 | 407843 | 8 |
| `dd_deep_chainmap_eq` | ok | 760 | 519227 | 61 |
| `de_deep_exception_repr` | ok | 70 | 306264 | 10 |
| `g1_gzip_bomb` | budget MaxStrChars | 5 | 18 | 0 |
| `g2_zlib_bomb` | budget MaxStrChars | 12 | 18 | 0 |
| `h1_sha224` | ok | 438 | 3930 | 15 |
| `h2_pbkdf2_iters` | budget DeadlineMs | 20004 | 1998848 | 0 |
| `j1_invalid_tail` | ok | 686 | 1505890 | 28 |
| `j2_valid_big` | ok | 454 | 1500032 | 28 |
| `j3_many_spaces` | ok | 176 | 400031 | 30 |
| `j4_many_strings` | ok | 231 | 400031 | 43 |
| `j5_dumps_big` | ok | 931 | 3000031 | 20 |
| `j6_deep_nested` | ok | 2 | 546 | 0 |
| `j7_parse_float_hook` | ok | 1586 | 900033 | 38 |
| `l1_reentry_deque_repr` | ok | 353 | 1025655 | 32 |
| `l2_reentry_exc_repr` | ok | 273 | 612514 | 20 |
| `l3_reentry_slice_str` | ok | 313 | 612514 | 15 |
| `l4_reentry_asdict` | ok | 348 | 612532 | 14 |
| `l5_partial_chain` | budget MaxAllocBytes | 453 | 91352 | 1024 |
| `l6_partial_big_args_calls` | budget DeadlineMs | 3017 | 92468777 | 40 |
| `l7_partial_kw_merge` | ok | 1565 | 4287428 | 143 |
| `l8_sleep_past_total` | budget SleepTotalMs | 15 | 18 | 0 |
| `l9_int_overlong` | budget MaxIntBits | 18 | 247 | 1 |
| `la_int_base36_long` | budget DeadlineMs | 3022 | 35256485 | 0 |
| `lb_bit_length_1m` | ok | 15 | 20 | 0 |
| `lc_decimal_overlong` | ok | 42 | 273 | 1 |
| `ld_decimal_long_fraction` | ok | 44 | 266 | 3 |
| `le_fraction_overlong` | budget MaxIntBits | 35 | 264 | 1 |
| `lf_fraction_power_of_ten` | budget MaxIntBits | 3 | 20 | 0 |
| `lg_fraction_pow` | budget MaxIntBits | 27 | 20 | 0 |
| `lh_fraction_big_parts` | budget DeadlineMs | 3029 | 1880 | 0 |
| `li_modpow_huge` | budget DeadlineMs | 3008 | 5496686 | 0 |
| `lj_modinverse_huge` | budget DeadlineMs | 3001 | 3 | 0 |
| `lk_isqrt_1m` | ok | 784 | 40 | 0 |
| `ll_gcd_huge` | budget DeadlineMs | 3126 | 29696 | 0 |
| `lm_str_huge` | budget DeadlineMs | 3005 | 320412175 | 0 |
| `ln_from_bytes_huge` | budget MaxIntBits | 279 | 2449 | 9 |
| `lo_to_bytes_big` | ok | 639 | 14 | 0 |
| `lp_to_bytes_len_cap` | budget MaxStrChars | 1 | 2 | 0 |
| `lq_round_fraction_far` | ok | 6 | 25 | 0 |
| `lr_pow_exact_cap` | ok | 3 | 11 | 0 |
| `m1_stdev_decimal` | ok | 757 | 1814193 | 28 |
| `m2_median_decimal` | ok | 302 | 1814175 | 29 |
| `m3_decimal_ln_loop` | ok | 15648 | 20136658 | 168 |
| `m4_decimal_extremes` | ok | 8 | 188 | 0 |
| `p1_eq_recurses` | budget MaxCallDepth | 7 | 608 | 0 |
| `p2_hash_recurses` | budget MaxCallDepth | 14 | 408 | 0 |
| `p3_str_recurses` | budget MaxCallDepth | 7 | 408 | 0 |
| `p4_len_recurses` | budget MaxCallDepth | 5 | 408 | 0 |
| `p5_bool_recurses` | budget MaxCallDepth | 5 | 408 | 0 |
| `p6_iter_never_ends` | budget MaxCollectionItems | 1292 | 15078134 | 381 |
| `p7_getitem_never_ends` | budget MaxCollectionItems | 5163 | 15078132 | 586 |
| `p8_contains_recurses` | budget MaxCallDepth | 5 | 405 | 0 |
| `p9_id_many` | ok | 2579 | 6062515 | 260 |
| `pa_call_recurses` | budget MaxCallDepth | 8 | 406 | 0 |
| `r1_compile_long` | raise error: regular expression t | 22 | 125086 | 1 |
| `r2_compile_named` | raise error: regular expression t | 1 | 12588 | 0 |
| `r3_counted_repeats` | raise error: regular expression t | 1 | 93836 | 1 |
| `s1_str_upper` | ok | 148 | 1969 | 30 |
| `s2_str_casefold` | ok | 222 | 1970 | 45 |
| `t1_struct_iter_unpack` | ok | 711 | 6000034 | 165 |
| `u1_quote` | ok | 198 | 3938 | 61 |
| `u2_unquote` | ok | 498 | 2001983 | 30 |
| `u3_parse_qs` | ok | 1621 | 3000022 | 95 |
| `u4_urlencode` | ok | 1167 | 4142543 | 107 |
| `u5_urlsplit_long` | ok | 87 | 1006 | 22 |
| `u6_urljoin_taught` | ok | 433 | 2166345 | 23 |
| `v1_reader_many_fields` | ok | 908 | 4000035 | 80 |
| `v2_reader_huge_field` | ok | 306 | 8000038 | 61 |
| `v3_writer_many_fields` | ok | 640 | 2002008 | 38 |
| `v4_sniff_big` | ok | 814 | 837538 | 4 |
| `v5_has_header_big` | ok | 838 | 837710 | 9 |
| `v6_dictreader_rows` | ok | 517 | 4000038 | 216 |
| `v7_duck_dialect_loop` | ok | 951 | 3043777 | 85 |
| `w1_html_unescape` | ok | 383 | 1983 | 33 |
| `w2_strftime_long_fmt` | ok | 410 | 31282 | 24 |
| `w3_strptime_long` | ok | 69 | 26 | 7 |
| `w4_fromisoformat_long` | ok | 67 | 25 | 11 |
| `w5_percent_format_many` | ok | 462 | 3925704 | 39 |
| `w6_textwrap_big` | ok | 6549 | 2062527 | 21 |
| `w7_bisect_insort` | ok | 16332 | 12277597 | 14 |
| `w8_heapq_big` | ok | 921 | 72000070 | 200 |
| `w9_deque_rotate` | ok | 380 | 1000276 | 49 |
| `wa_calendar_year` | raise AttributeError: module 'cale | 8 | 20 | 0 |
| `wb_unicodedata_lookup_loop` | ok | 984 | 2160966 | 52 |
| `wc_operator_itemgetter` | ok | 142 | 400038 | 16 |
| `wd_counter_most_common` | ok | 986 | 4000043 | 140 |
| `we_bytes_fromhex` | ok | 119 | 1967 | 19 |
| `wf_int_str_huge` | budget MaxIntBits | 1 | 0 | 0 |
| `x1_fromstring_flat` | ok | 1995 | 1500030 | 120 |
| `x2_tostring_html_wide` | ok | 2134 | 2039098 | 92 |
| `x3_tostring_text_wide` | ok | 2238 | 2046909 | 89 |
| `x4_deep_built_tostring` | ok | 837 | 1015660 | 38 |
| `x5_deep_built_iter` | ok | 961 | 1015662 | 41 |
| `x6_deep_built_itertext` | ok | 919 | 1225038 | 35 |
| `x7_deep_built_parentmap` | ok | 1281 | 1215661 | 38 |
| `x8_deep_built_indent` | budget MaxAllocBytes | 2888 | 627025 | 1024 |
| `x9_deep_built_xmlid` | ok | 1315 | 2963223 | 60 |
| `xa_deep_built_deepcopy` | ok | 544 | 615669 | 35 |
| `xb_xpath_text_quadratic` | ok | 437 | 390029 | 14 |
| `xc_xmlid_big` | ok | 1963 | 1802730 | 179 |
| `xd_iterparse_big` | ok | 2409 | 2501031 | 192 |
| `xe_find_wide` | ok | 1321 | 1000027 | 96 |
| `z1_zip_deflate_bomb` | budget MaxAllocBytes | 760 | 55021 | 71 |
| `z2_zip_nested` | budget MaxAllocBytes | 484 | 55404 | 71 |
| `z3_zip_many_entries` | budget MaxStrChars | 1846 | 1522600 | 118 |
| `z4_zip_overlap` | ok | 14 | 117 | 0 |
| `z5_zip_forged_size` | ok | 367 | 36749 | 43 |
| `z6_zip_not_an_archive` | ok | 254 | 3991 | 30 |
