import statistics as st


def err(f):
    try:
        f()
        return 'ok'
    except Exception as e:
        return type(e).__name__


out = []
out.append(err(lambda: st.mean([])))
out.append(err(lambda: st.median([])))
out.append(err(lambda: st.variance([1])))
out.append(err(lambda: st.stdev([])))
out.append(err(lambda: st.pvariance([])))
out.append(err(lambda: st.mode([])))
out.append(err(lambda: st.mean(['a', 'b'])))
out.append(issubclass(st.StatisticsError, ValueError))
result = out
