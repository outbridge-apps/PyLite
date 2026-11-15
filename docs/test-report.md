# Test report

Written by `tools/run-gates.ps1`; run it to repeat every number below (see `TESTING.md`).

| | |
|---|---|
| PyLite | 1.2.0, commit `da737b9` |
| run | 2026-11-15 00:06 to 00:37 UTC |
| OS | Microsoft Windows Server 2022 Datacenter Evaluation (10.0.20348) |
| CPU | Intel(R) Xeon(R) CPU E5-2650 v2 @ 2.60GHz, 8 logical processors |
| .NET | Framework 4.8 (release 528449), SDK 9.0.308 |
| reference | IronPython 3.4.2 (NuGet) |
| xUnit | 2.9.2 |

## Gates

| gate | result | outcome | time |
|---|---|---|---:|
| build (PyLite.sln, Release) | pass | warnings as errors in the engine | 21 s |
| xUnit, everyday (Category!=MachineSensitive) | pass | 8349 passed, 0 failed of 8349 | 224 s |
| xUnit, MachineSensitive | pass | 253 passed, 0 failed of 253 | 207 s |
| conformance (CPython Lib/test cases) | pass | 174 passed, 0 failed, 0 skipped | 12 s |
| differential (vs IronPython 3.4.2) | pass | 90 scripts: 69 match, 21 known difference(s), 0 bug(s) | 25 s |
| adversarial (Stress corpus) | pass | 274 probes, the sandbox held on every one | 351 s |
| probe bank (reviewers' probes) | pass | 1200 probes on their pins (outcome, output, steps, charge; 1.1.0 ceiling) | 1,005 s |

## Differential: PyLite and IronPython 3.4.2 on the same scripts

Each script sets `result`; the two are compared by `repr` (a `dict` order-free, floats by their bits), or by
the exception class when the script raises. A known difference is listed with its reason in
`tools/PyLite.DiffHarness/known-differences.txt`.

| script | class | detail |
|---|---|---|
| float_floordiv_iron.py | Known | value differs (known): (9.0, -1.0, 9.0, -1.0) vs (10.0, -0.0, 9.0, -1.0) |
| iron34/aware_naive_eq.py | Known | outcome differs (known) |
| iron34/decimal_bad_rounding.py | Known | value differs (known): TypeError vs 'KeyError' |
| iron34/factorial_float.py | Known | value differs (known): TypeError vs 'ValueError' |
| iron34/float_text.py | Known | value differs (known): (2.67, '1e+16', '1e-05') vs (2.6800000000000002, '10000000000000000.0', '1.0000000000000001e-05') |
| iron34/json_decode_error.py | Known | value differs (known): ('JSONDecodeError', True) vs ('ValueError', True) |
| iron34/keyerror_str.py | Known | value differs (known): ("'k'", '1', '') vs ('k', '1', '') |
| iron34/math_log10.py | Known | value differs (known): [3.0, 6.0] vs [2.9999999999999996, 5.9999999999999991] |
| iron34/re_empty_matches.py | Known | value differs (known): ('-b--c-', ['', 'a', '', 'b', 'c', '']) vs ('-b-c-', ['a', 'bc']) |
| iron34/re_escape.py | Known | value differs (known): ['1\\+1=2', 'a\\-b\\ c', 'x_y'] vs ['1\\+1\\=2', 'a\\-b\\ c', 'x_y'] |
| iron34/re_lastgroup.py | Known | value differs (known): (None, 'x') vs ('1', 'x') |
| iron34/re_pattern_error.py | Known | value differs (known): PatternError vs 'error' |
| iron34/re_sub_bad_group.py | Known | value differs (known): error vs '\x02' |
| iron34/statistics_mode_tie.py | Known | value differs (known): 1 vs 'StatisticsError' |
| iron34/str_isnumeric.py | Known | value differs (known): [True, True] vs [False, False] |
| iron34/struct_float32.py | Known | value differs (known): 0.10000000149011612 vs 0.1 |
| iron34/sum_compensated.py | Known | value differs (known): (1.0, 6.6, 2.0) vs (0.99999999999999989, 6.6000000000000005, 1.0) |
| iron34/textwrap_hyphens.py | Known | value differs (known): ['a-b-', 'c-', 'd-e'] vs ['a-b-', 'c-d-', 'e'] |
| iron34/timedelta_repr.py | Known | value differs (known): datetime.timedelta(days=-1, seconds=82800) vs 'datetime.timedelta(-1, 82800)' |
| iron34/unicode_case.py | Known | value differs (known): ['Ç…', True] vs ['Ç„', False] |
| iron34/url_quote_tilde.py | Known | value differs (known): ~user/a%20b vs '%7Euser/a%20b' |
| arith.py | Match |  |
| bytesio.py | Match |  |
| class_consts.py | Match |  |
| collections/counter.py | Match |  |
| collections/defaultdict_ops.py | Match |  |
| collections/deque_ops.py | Match |  |
| collections/namedtuple_ops.py | Match |  |
| collections/ordered_chain.py | Match |  |
| comprehension.py | Match |  |
| contextlib_decimal.py | Match |  |
| core/builtins_more.py | Match |  |
| core/classes_closures.py | Match |  |
| core/containers_slicing.py | Match |  |
| core/errors_builtin.py | Match |  |
| core/exceptions_flow.py | Match |  |
| csv/dict_rw.py | Match |  |
| csv/reader_dialects.py | Match |  |
| csv/sniffer.py | Match |  |
| csv/writer_quoting.py | Match |  |
| datetime/arith.py | Match |  |
| datetime/calendar_math.py | Match |  |
| datetime/errors.py | Match |  |
| datetime/format.py | Match |  |
| datetime/timezones.py | Match |  |
| decimal/money.py | Match |  |
| dict_order.py | Match |  |
| email_dates.py | Match |  |
| encoding/codecs_hashes.py | Match |  |
| encoding/struct_ops.py | Match |  |
| enum_basics.py | Match |  |
| error_div.py | Match |  |
| float_divmod.py | Match |  |
| float_repr.py | Match |  |
| fractions/ops.py | Match |  |
| functools/reduce_partial_cache.py | Match |  |
| int_big.py | Match |  |
| itertools/combinatorics.py | Match |  |
| itertools/groupby_errors.py | Match |  |
| itertools/slicing_filters.py | Match |  |
| json/errors.py | Match |  |
| json/options.py | Match |  |
| json_rt.py | Match |  |
| math/functions.py | Match |  |
| math_fns.py | Match |  |
| re/errors.py | Match |  |
| re/findall_sub.py | Match |  |
| re/lookaround_escape.py | Match |  |
| re/search_groups.py | Match |  |
| re/split_flags.py | Match |  |
| re/unicode_classes.py | Match |  |
| sorted_key.py | Match |  |
| statistics/central_spread.py | Match |  |
| statistics/errors.py | Match |  |
| stdlib/small_modules.py | Match |  |
| str_format.py | Match |  |
| string_template.py | Match |  |
| strings.py | Match |  |
| super_chain.py | Match |  |
| text/str_methods.py | Match |  |
| text/textwrap_ops.py | Match |  |
| try_else_raise.py | Match |  |
| try_match_error_finally.py | Match |  |
| urllib/quote_query.py | Match |  |
| urllib/split_join.py | Match |  |
| with_stmt.py | Match |  |
| xml/build_errors.py | Match |  |
| xml/parse_find.py | Match |  |
| zipfile_rt.py | Match |  |
| zlib_gzip.py | Match |  |

## Adversarial: which limit stopped each probe

`ok` and `raise` mean the script finished or raised a Python exception it was meant to raise; `budget X`
names the limit that stopped it. A probe fails the gate when it ends in a host error, an engine fault or
a throw, returns more than 500 ms past its deadline, leaks its runner thread, grows the process past four
times its memory limit plus 512 MB, keeps a core busy in the quarter second after it returned, or ends in
another kind of outcome than `bombs/expected.txt` pins. The probes and their limits are in `tools/PyLite.Stress/bombs`;
the groups in its README.

| probe | outcome | ms | steps | MB charged | MB peak |
|---|---|---:|---:|---:|---:|
| `a1_r6_xml_find_unclosed_braces` | ok | 405 | 59 | 0 | 3 |
| `a2_r6_xml_tostring_long_ns_tag` | ok | 500 | 21539 | 26 | 139 |
| `a3_r6_hash_one_byte_updates` | ok | 582 | 4065014 | 43 | 9 |
| `a4_r6_counter_repr_million` | ok | 1153 | 12406324 | 233 | 116 |
| `a5_r6_super_init_million` | ok | 1325 | 7171884 | 201 | 1 |
| `a6_r6_int_hex_long` | ok | 26 | 3732 | 0 | 0 |
| `a7_r6_sleep_sub_ms` | ok | 2108 | 8146 | 0 | 0 |
| `b1_bytes_upper` | ok | 64 | 1967 | 15 | 15 |
| `b2_bytes_split` | ok | 726 | 2002456 | 135 | 171 |
| `b3_bytes_replace` | ok | 199 | 2002457 | 32 | 32 |
| `b4_bytes_title` | ok | 92 | 1967 | 15 | 15 |
| `b5_bytes_isalpha` | ok | 86 | 1958 | 7 | 7 |
| `c1_encode_cp866` | ok | 205 | 3923 | 22 | 22 |
| `c2_decode_cp866` | ok | 231 | 3926 | 22 | 8 |
| `c3_encode_utf8` | ok | 58 | 3924 | 30 | 20 |
| `c4_decode_utf16` | ok | 124 | 3928 | 15 | 19 |
| `c5_encode_replace` | ok | 222 | 8003923 | 38 | 22 |
| `d1_deep_list_repr` | ok | 234 | 704704 | 10 | 29 |
| `d2_deep_list_eq` | ok | 95 | 109514 | 16 | 15 |
| `d3_deep_tuple_hash` | ok | 39 | 104827 | 7 | 1 |
| `d4_deep_deepcopy` | ok | 191 | 304715 | 16 | 29 |
| `d5_deep_json_dumps` | ok | 75 | 104975 | 10 | 6 |
| `d6_deep_dataclass_eq` | ok | 289 | 512659 | 9 | 12 |
| `d7_deep_dataclass_hash` | ok | 132 | 306413 | 6 | 5 |
| `d8_deep_record_repr` | ok | 262 | 810954 | 15 | 19 |
| `d9_deep_deque_eq` | ok | 305 | 915784 | 29 | 29 |
| `da_deep_deque_repr` | ok | 120 | 508034 | 16 | 8 |
| `db_deep_slice_repr` | ok | 53 | 306264 | 7 | 0 |
| `dc_deep_partial_repr` | ok | 106 | 407843 | 8 | 13 |
| `dd_deep_chainmap_eq` | ok | 559 | 519099 | 61 | 80 |
| `de_deep_exception_repr` | ok | 64 | 306264 | 10 | 10 |
| `e1_sof_defaultdict_self_factory` | ok | 6 | 28 | 0 | 0 |
| `e2_sof_defaultdict_factory_chain` | ok | 2095 | 4570353 | 132 | 254 |
| `e3_sof_except_nested_tuple` | ok | 416 | 732832 | 55 | 48 |
| `e4_mem_itemgetter_chain` | budget DeadlineMs | 3005 | 5027001 | 167 | 356 |
| `e5_mem_lru_wrapper_chain` | budget MaxAllocBytes | 2548 | 8310187 | 256 | 165 |
| `e6_mem_cmp_to_key_chain` | budget MaxAllocBytes | 2386 | 8310187 | 256 | 82 |
| `e7_mem_format_precision_kept` | budget MaxAllocBytes | 344 | 81 | 259 | 240 |
| `e8_mem_finditer_endpos_kept` | ok | 1806 | 711 | 7 | 8 |
| `e9_mem_dumps_separator_per_depth` | budget MaxAllocBytes | 259 | 256 | 259 | 255 |
| `ea_mem_dumps_indent_per_depth` | ok | 311 | 650 | 212 | 185 |
| `eb_mem_class_fields` | budget DeadlineMs | 3001 | 3562496 | 196 | 245 |
| `ec_cap_namedtuple_defaults_repeat` | budget MaxCollectionItems | 690 | 10000039 | 64 | 102 |
| `ed_cap_dictwriter_fieldnames_repeat` | budget MaxCollectionItems | 739 | 10000056 | 64 | 101 |
| `ee_cap_join_empty_repeat` | budget MaxCollectionItems | 1012 | 16777239 | 64 | 127 |
| `ef_cap_writerow_repeat_none` | budget MaxStrChars | 2149 | 16000058 | 30 | 25 |
| `f1_leak_fused_or_filter` | budget DeadlineMs | 3370 | 1136533 | 0 | 51 |
| `f2_leak_compiled_sum` | budget DeadlineMs | 3364 | 606252 | 0 | 74 |
| `f3_leak_if_clauses` | budget DeadlineMs | 3223 | 35840 | 0 | 75 |
| `f4_leak_kwonly_defaults` | budget MaxAllocBytes | 1194 | 571731 | 256 | 50 |
| `f5_leak_class_init_walk` | budget MaxAllocBytes | 1597 | 2825323 | 256 | 25 |
| `f6_leak_list_del_front` | budget DeadlineMs | 3010 | 411835 | 38 | 38 |
| `f7_leak_startswith_prefixes` | budget DeadlineMs | 3001 | 1445962 | 61 | 45 |
| `f8_leak_bytes_tuple_eq` | budget DeadlineMs | 3001 | 316416 | 45 | 45 |
| `f9_leak_slice_indices_big` | budget DeadlineMs | 3002 | 17 | 0 | 3 |
| `fa_leak_bigint_floordiv` | budget DeadlineMs | 3003 | 956 | 0 | 3 |
| `fb_leak_asdict_dag` | budget MaxAllocBytes | 2730 | 894941 | 256 | 310 |
| `fc_leak_randbytes` | budget MaxAllocBytes | 1731 | 35062 | 257 | 42 |
| `fd_leak_unescape_legacy` | budget MaxAllocBytes | 1460 | 11008 | 256 | 73 |
| `fe_leak_attrgetter_path` | budget DeadlineMs | 3001 | 10025 | 120 | 157 |
| `ff_leak_deque_imul_maxlen` | budget DeadlineMs | 3125 | 2019105 | 245 | 108 |
| `fg_leak_capwords_words` | ok | 1775 | 8000052 | 91 | 91 |
| `fh_cap_match_star_range` | budget MaxCollectionItems | 46 | 3 | 0 | 0 |
| `g1_gzip_bomb` | budget MaxStrChars | 2 | 18 | 0 | 0 |
| `g2_zlib_bomb` | budget MaxStrChars | 4 | 18 | 0 | 0 |
| `h1_sha224` | ok | 443 | 3930 | 15 | 7 |
| `h2_pbkdf2_iters` | budget DeadlineMs | 20002 | 1819648 | 0 | 1 |
| `j1_invalid_tail` | ok | 511 | 1506623 | 28 | 50 |
| `j2_valid_big` | ok | 440 | 1500032 | 28 | 50 |
| `j3_many_spaces` | ok | 176 | 400031 | 30 | 42 |
| `j4_many_strings` | ok | 289 | 400031 | 43 | 70 |
| `j5_dumps_big` | ok | 617 | 4500032 | 22 | 18 |
| `j6_deep_nested` | ok | 2 | 547 | 0 | 0 |
| `j7_parse_float_hook` | ok | 770 | 900033 | 38 | 29 |
| `k1_leak_float_parse` | budget DeadlineMs | 3047 | 83 | 30 | 30 |
| `k2_leak_decimal_invalid` | budget DeadlineMs | 3088 | 307 | 30 | 192 |
| `k3_leak_str_in` | budget DeadlineMs | 3010 | 336270 | 15 | 15 |
| `k4_leak_sort_long_str` | budget DeadlineMs | 3001 | 1632256 | 91 | 91 |
| `k5_leak_set_subset` | budget DeadlineMs | 3095 | 1030146 | 140 | 91 |
| `k6_leak_chainmap_in` | budget DeadlineMs | 3097 | 4024470 | 91 | 122 |
| `k7_leak_isalnum_astral` | budget DeadlineMs | 3006 | 23462 | 30 | 61 |
| `k8_leak_stringio_readline` | budget MaxAllocBytes | 1537 | 10372 | 1037 | 128 |
| `k9_leak_tee_clones` | ok | 1851 | 10024 | 217 | 164 |
| `ka_leak_kw_binding` | raise TypeError: f() missing 10000 re | 892 | 3253545 | 89 | 81 |
| `kb_leak_xml_cycle_iter` | budget MaxCollectionItems | 374 | 625024 | 57 | 111 |
| `kc_leak_hmac_long_key` | budget DeadlineMs | 3000 | 1324244 | 25 | 15 |
| `kd_leak_uuid_parse` | budget DeadlineMs | 3012 | 242 | 30 | 76 |
| `ke_leak_csv_terminator` | ok | 16 | 140 | 3 | 0 |
| `kf_leak_namedtuple_fields` | budget MaxAllocBytes | 863 | 18 | 1247 | 220 |
| `kg_leak_bigint_mod` | budget DeadlineMs | 3001 | 3706 | 0 | 3 |
| `kh_leak_notes_format` | ok | 80 | 306322 | 10 | 5 |
| `l1_reentry_deque_repr` | ok | 392 | 1030655 | 32 | 28 |
| `l2_reentry_exc_repr` | ok | 297 | 612514 | 20 | 28 |
| `l3_reentry_slice_str` | ok | 196 | 612514 | 15 | 10 |
| `l4_reentry_asdict` | ok | 327 | 622534 | 14 | 15 |
| `l5_partial_chain` | budget MaxAllocBytes | 438 | 91352 | 1024 | 3 |
| `l6_partial_big_args_calls` | budget DeadlineMs | 3000 | 82402857 | 48 | 75 |
| `l7_partial_kw_merge` | ok | 1426 | 4287428 | 143 | 118 |
| `l8_sleep_past_total` | budget SleepTotalMs | 1 | 18 | 0 | 0 |
| `l9_int_overlong` | raise ValueError: Exceeds the limit (4 | 7 | 251 | 1 | 1 |
| `la_int_base32_long` | ok | 4 | 2971 | 0 | 0 |
| `lb_bit_length_1m` | ok | 4 | 20 | 0 | 0 |
| `lc_decimal_overlong` | ok | 10 | 273 | 1 | 1 |
| `ld_decimal_long_fraction` | ok | 26 | 266 | 3 | 7 |
| `le_fraction_overlong` | raise ValueError: Exceeds the limit (4 | 23 | 268 | 1 | 0 |
| `lf_fraction_power_of_ten` | budget MaxIntBits | 2 | 20 | 0 | 0 |
| `lg_fraction_pow` | budget MaxIntBits | 19 | 20 | 0 | 0 |
| `lh_fraction_big_parts` | budget DeadlineMs | 3001 | 1880 | 0 | 3 |
| `li_modpow_huge` | budget MaxSteps | 6 | 2147483649 | 0 | 0 |
| `lj_modinverse_huge` | budget DeadlineMs | 3000 | 1836 | 0 | 2 |
| `lk_isqrt_1m` | ok | 495 | 40 | 0 | 0 |
| `ll_gcd_huge` | ok | 2089 | 62484 | 0 | 1 |
| `lm_str_huge` | raise ValueError: Exceeds the limit (4 | 71 | 6 | 0 | 0 |
| `ln_from_bytes_huge` | budget MaxIntBits | 260 | 2449 | 9 | 25 |
| `lo_to_bytes_big` | ok | 52 | 14 | 0 | 0 |
| `lp_to_bytes_len_cap` | budget MaxStrChars | 0 | 2 | 0 | 0 |
| `lq_round_fraction_far` | ok | 8 | 26 | 0 | 0 |
| `lr_pow_exact_cap` | ok | 22 | 11 | 0 | 0 |
| `m1_stdev_decimal` | ok | 610 | 1814193 | 28 | 18 |
| `m2_median_decimal` | ok | 274 | 1814175 | 29 | 18 |
| `m3_decimal_ln_loop` | ok | 16947 | 20136658 | 168 | 0 |
| `m4_decimal_extremes` | ok | 7 | 190 | 0 | 0 |
| `n1_native_float_format` | ok | 16 | 9 | 1 | 0 |
| `n2_native_decimal_format` | budget MaxStrChars | 5 | 21 | 0 | 0 |
| `n3_native_decimal_zero_exp` | ok | 3 | 22 | 0 | 0 |
| `n4_native_csv_sniff` | budget RegexPerOpTimeout | 253 | 1005 | 0 | 0 |
| `n5_native_intenum_str` | raise ValueError: Exceeds the limit (4 | 19 | 265 | 1 | 0 |
| `n6_native_capwords_sep` | ok | 489 | 4204 | 91 | 149 |
| `n7_native_pbkdf2_salt` | budget DeadlineMs | 3015 | 97693 | 30 | 30 |
| `n8_native_fraction_str` | raise ValueError: Exceeds the limit (4 | 25 | 1002 | 0 | 2 |
| `n9_native_textwrap_split` | budget MaxCollectionItems | 1389 | 5000019 | 192 | 287 |
| `na_native_seed_str` | ok | 327 | 7833 | 30 | 122 |
| `nb_native_qsl_sep` | ok | 151 | 264 | 30 | 30 |
| `nc_native_regex_repeat` | ok | 9 | 83 | 0 | 0 |
| `nd_native_range_step` | ok | 844 | 2 | 0 | 1 |
| `ne_native_variance` | budget MaxIntBits | 73 | 3931 | 1 | 3 |
| `o1_mem_ascii_expand` | budget MaxStrChars | 598 | 4 | 60 | 89 |
| `o2_mem_format_g` | budget MaxStrChars | 2 | 9 | 0 | 0 |
| `o3_mem_print_sep` | budget MaxStrChars | 19 | 2 | 30 | 3 |
| `o4_mem_json_escape` | budget MaxStrChars | 139 | 21 | 30 | 51 |
| `o5_mem_strftime_zone` | budget MaxStrChars | 87 | 27 | 1 | 3 |
| `o6_mem_format_fields` | budget MaxStrChars | 36 | 20 | 1 | 23 |
| `o7_mem_fstring_cap` | budget MaxStrChars | 59 | 0 | 61 | 30 |
| `o8_mem_star_args` | budget MaxCollectionItems | 383 | 5000019 | 64 | 100 |
| `o9_mem_dist_iterator` | budget MaxCollectionItems | 343 | 10000038 | 64 | 100 |
| `oa_mem_re_endpos_kept` | ok | 4 | 2604 | 1 | 1 |
| `ob_mem_re_groups_kept` | ok | 1849 | 6949 | 244 | 329 |
| `oc_mem_json_big_list` | ok | 2039 | 15000023 | 76 | 73 |
| `od_mem_repr_big_list` | ok | 1421 | 15000008 | 104 | 128 |
| `oe_mem_xml_extend` | budget MaxCollectionItems | 736 | 1982 | 155 | 155 |
| `of_mem_attrgetter` | budget MaxAllocBytes | 445 | 18 | 257 | 200 |
| `og_mem_urljoin` | budget MaxAllocBytes | 210 | 3692 | 486 | 57 |
| `oh_mem_csv_field` | raise Error: field larger than fi | 28 | 1000031 | 1 | 1 |
| `p1_eq_recurses` | budget MaxCallDepth | 6 | 610 | 0 | 0 |
| `p2_hash_recurses` | budget MaxCallDepth | 8 | 410 | 0 | 0 |
| `p3_str_recurses` | budget MaxCallDepth | 6 | 410 | 0 | 0 |
| `p4_len_recurses` | budget MaxCallDepth | 6 | 410 | 0 | 0 |
| `p5_bool_recurses` | budget MaxCallDepth | 6 | 410 | 0 | 0 |
| `p6_iter_never_ends` | budget MaxCollectionItems | 1561 | 15078136 | 381 | 127 |
| `p7_getitem_never_ends` | budget MaxCollectionItems | 4253 | 15078134 | 586 | 257 |
| `p8_contains_recurses` | budget MaxCallDepth | 6 | 407 | 0 | 0 |
| `p9_id_many` | ok | 2164 | 6062517 | 260 | 101 |
| `pa_call_recurses` | budget MaxCallDepth | 4 | 408 | 0 | 0 |
| `q1_review_star_nesting` | SYNTAX expression too deeply nested (limit 64) | 6 | 0 | 0 | 0 |
| `q2_review_exc_str_nested` | ok | 177 | 612514 | 20 | 21 |
| `q3_review_repeat_repr_nested` | ok | 287 | 612528 | 15 | 18 |
| `q4_review_counter_nested_le` | ok | 480 | 528414 | 44 | 47 |
| `q5_review_cause_cycle_notes` | raise ValueError:  | 15 | 33 | 22 | 3 |
| `r1_compile_long` | raise PatternError: regular expression t | 1 | 125086 | 1 | 1 |
| `r2_compile_named` | raise PatternError: regular expression t | 9 | 12588 | 0 | 0 |
| `r3_counted_repeats` | raise PatternError: regular expression t | 4 | 93836 | 1 | 2 |
| `r4_decimal_format_wide` | budget MaxAllocBytes | 851 | 82 | 272 | 176 |
| `r4_del_big` | budget DeadlineMs | 1005 | 84322 | 38 | 38 |
| `r4_fold_big` | budget DeadlineMs | 2000 | 413424 | 6 | 0 |
| `r4_json_sort_long` | budget MaxAllocBytes | 2111 | 621889 | 265 | 103 |
| `r4_normalize_marks` | ok | 1164 | 3938 | 244 | 233 |
| `r4_union_wide` | budget DeadlineMs | 3000 | 104866 | 35 | 2 |
| `r5_codec_huge` | ok | 24 | 9 | 0 | 0 |
| `r5_indent_level` | ok | 17 | 28 | 0 | 0 |
| `r5_regex_comment` | ok | 5 | 86 | 0 | 0 |
| `r5_replace_many` | ok | 229 | 869194 | 21 | 30 |
| `r5_spec_format` | budget MaxStrChars | 38 | 3 | 76 | 33 |
| `r5_spec_nested` | budget MaxStrChars | 41 | 0 | 76 | 36 |
| `r5_str_huge_in_try` | ok | 84 | 40 | 0 | 0 |
| `r5_timedelta_huge` | ok | 37 | 46 | 0 | 0 |
| `s1_err_xml_text_int` | ok | 1 | 31 | 0 | 0 |
| `s1_str_upper` | ok | 173 | 1969 | 30 | 30 |
| `s2_err_xml_attrkey_int` | ok | 1 | 30 | 0 | 0 |
| `s2_str_casefold` | ok | 235 | 1970 | 45 | 55 |
| `s3_err_xml_child_int` | ok | 3 | 29 | 0 | 0 |
| `s4_err_xml_tail_textpred` | ok | 12 | 36 | 0 | 0 |
| `s5_err_decimal_noarg` | ok | 1 | 30 | 0 | 0 |
| `s6_err_quantiles_overflow` | ok | 48 | 1930043 | 11 | 4 |
| `s7_err_re_verbose_twin` | ok | 9 | 89 | 0 | 0 |
| `s8_err_re_pos_after_endpos` | ok | 1 | 97 | 0 | 0 |
| `s9_err_normalize_nonchar` | ok | 126 | 1001 | 7 | 7 |
| `sa_err_zip_header_offset` | ok | 89 | 72 | 0 | 1 |
| `sb_cap_struct_zero_s` | budget MaxCollectionItems | 326 | 5000019 | 22 | 144 |
| `sc_cap_cwr_tuple` | budget MaxCollectionItems | 3 | 19 | 0 | 0 |
| `sd_cap_product_repeat` | budget MaxCollectionItems | 3 | 19 | 0 | 0 |
| `se_cap_list_slice_assign` | budget MaxCollectionItems | 1161 | 4000001 | 91 | 122 |
| `sf_cap_list_insert` | budget MaxCollectionItems | 517 | 2 | 38 | 38 |
| `t1_struct_iter_unpack` | ok | 598 | 6000034 | 99 | 7 |
| `u1_quote` | ok | 197 | 5892 | 38 | 58 |
| `u2_unquote` | ok | 491 | 2001983 | 30 | 38 |
| `u3_parse_qs` | ok | 1124 | 4000023 | 64 | 88 |
| `u4_urlencode` | ok | 901 | 4142543 | 88 | 56 |
| `u5_urlsplit_long` | ok | 67 | 1006 | 22 | 31 |
| `u6_urljoin_taught` | ok | 302 | 2166345 | 23 | 19 |
| `v1_reader_many_fields` | ok | 934 | 4000035 | 80 | 183 |
| `v2_reader_huge_field` | ok | 185 | 8000038 | 61 | 57 |
| `v3_writer_many_fields` | ok | 485 | 2002008 | 45 | 27 |
| `v4_sniff_big` | ok | 901 | 837538 | 20 | 27 |
| `v5_has_header_big` | ok | 903 | 837710 | 25 | 27 |
| `v6_dictreader_rows` | ok | 559 | 4000038 | 200 | 3 |
| `v7_duck_dialect_loop` | ok | 1077 | 3043779 | 85 | 0 |
| `w1_html_unescape` | ok | 232 | 1983 | 33 | 32 |
| `w2_strftime_long_fmt` | ok | 304 | 31282 | 24 | 13 |
| `w3_strptime_long` | ok | 60 | 26 | 11 | 3 |
| `w4_fromisoformat_long` | ok | 72 | 25 | 15 | 5 |
| `w5_percent_format_many` | ok | 287 | 3925704 | 39 | 24 |
| `w6_textwrap_big` | ok | 3491 | 2062527 | 91 | 113 |
| `w7_bisect_insort` | ok | 17155 | 12277597 | 14 | 5 |
| `w8_heapq_big` | ok | 1084 | 72000072 | 200 | 179 |
| `w9_deque_rotate` | ok | 310 | 1000276 | 49 | 29 |
| `wa_calendar_year` | raise AttributeError: module 'calendar' ha | 8 | 20 | 0 | 0 |
| `wb_unicodedata_lookup_loop` | ok | 826 | 2160966 | 52 | 0 |
| `wc_operator_itemgetter` | ok | 80 | 400038 | 19 | 6 |
| `wd_counter_most_common` | ok | 706 | 4000048 | 140 | 0 |
| `we_bytes_fromhex` | ok | 113 | 1967 | 19 | 30 |
| `wf_int_str_huge` | budget MaxIntBits | 0 | 0 | 0 | 0 |
| `x1_fromstring_flat` | ok | 1193 | 1501007 | 105 | 112 |
| `x2_tostring_html_wide` | ok | 1067 | 2601598 | 77 | 73 |
| `x3_tostring_text_wide` | ok | 968 | 2109408 | 74 | 49 |
| `x4_deep_built_tostring` | ok | 382 | 1040660 | 36 | 29 |
| `x5_deep_built_iter` | ok | 418 | 1040662 | 36 | 30 |
| `x6_deep_built_itertext` | ok | 382 | 1050037 | 38 | 27 |
| `x7_deep_built_parentmap` | ok | 579 | 865664 | 48 | 45 |
| `x8_deep_built_indent` | budget MaxAllocBytes | 3037 | 664629 | 1024 | 575 |
| `x9_deep_built_xmlid` | ok | 1182 | 3013223 | 87 | 109 |
| `xa_deep_built_deepcopy` | ok | 1056 | 1015671 | 56 | 72 |
| `xb_xpath_text_quadratic` | ok | 105 | 311401 | 13 | 10 |
| `xc_xmlid_big` | ok | 1573 | 1803948 | 219 | 184 |
| `xd_iterparse_big` | ok | 1384 | 2627008 | 165 | 133 |
| `xe_find_wide` | ok | 434 | 563017 | 81 | 68 |
| `y1_cap_quote_expand` | budget MaxStrChars | 41 | 4432 | 11 | 11 |
| `y2_cap_re_escape` | budget MaxStrChars | 28 | 4414 | 17 | 17 |
| `y3_cap_xml_attr_escape` | budget MaxStrChars | 13 | 23 | 5 | 0 |
| `y4_cap_normalize_expand` | budget MaxStrChars | 198 | 266 | 62 | 102 |
| `y5_cap_bytes_hex_sep` | budget MaxStrChars | 118 | 1467 | 5 | 0 |
| `y6_cap_encode_utf16` | budget MaxStrChars | 12 | 4398 | 17 | 4 |
| `y7_cap_replace_grow` | budget MaxStrChars | 172 | 5001468 | 43 | 43 |
| `y8_cap_b32encode` | budget MaxStrChars | 238 | 2704 | 10 | 9 |
| `y9_cap_csv_field_limit_raised` | budget MaxStrChars | 301 | 17000063 | 1 | 29 |
| `ya_mem_rsplit_parts` | budget MaxAllocBytes | 343 | 1036499 | 64 | 86 |
| `yb_mem_bytes_split_parts` | budget MaxAllocBytes | 333 | 790187 | 64 | 33 |
| `yc_mem_shorten_words` | budget MaxAllocBytes | 1558 | 1036514 | 64 | 121 |
| `yd_mem_parse_qs_parts` | ok | 649 | 8000036 | 30 | 0 |
| `ye_mem_counter_elements_kept` | ok | 426 | 1003080 | 158 | 23 |
| `yf_mem_xml_find_dag` | ok | 2 | 39 | 0 | 0 |
| `yg_mem_xml_iterfind_dag` | ok | 1820 | 34254536 | 0 | 0 |
| `yh_mem_xml_itertext_dag` | ok | 1783 | 34250532 | 0 | 0 |
| `yi_cap_xml_subelement` | budget MaxCollectionItems | 1 | 3101 | 0 | 0 |
| `yj_steps_re_sub_empty` | budget MaxSteps | 525 | 100001 | 7 | 0 |
| `yk_steps_re_split_empty` | budget MaxSteps | 1136 | 100001 | 29 | 53 |
| `yl_steps_decode_error_scan` | budget MaxSteps | 88 | 23510 | 30 | 0 |
| `ym_steps_b32encode` | budget MaxSteps | 825 | 11023 | 63 | 111 |
| `yn_cap_factorial` | budget MaxIntBits | 3 | 18 | 0 | 0 |
| `z1_zip_deflate_bomb` | budget MaxAllocBytes | 404 | 55021 | 71 | 51 |
| `z2_zip_nested` | budget MaxAllocBytes | 414 | 55404 | 71 | 50 |
| `z3_zip_many_entries` | budget MaxStrChars | 1407 | 1522600 | 118 | 66 |
| `z4_zip_overlap` | ok | 15 | 117 | 0 | 0 |
| `z5_zip_forged_size` | ok | 292 | 36753 | 43 | 0 |
| `z6_zip_not_an_archive` | ok | 250 | 3991 | 30 | 0 |
