using System;
using System.IO;
using Xunit;

namespace Outbridge.PyLite.Tests.Infra
{
    // The clone the tests read sources, docs and examples from: the first folder above the test dll that holds
    // PyLite.sln. Run from a copy outside the clone, a test says so instead of failing on a null.
    internal static class RepoPaths
    {
        internal static string Root()
        {
            string start = AppDomain.CurrentDomain.BaseDirectory;
            string dir = start;
            while (dir != null && !File.Exists(Path.Combine(dir, "PyLite.sln")))
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            if (dir == null)
                Assert.Fail("PyLite.sln was not found above " + start + ": this test reads the repository, so run it from a build inside the clone");
            return dir.TrimEnd(Path.DirectorySeparatorChar);
        }

        internal static string Combine(params string[] parts)
        {
            string path = Root();
            foreach (string p in parts)
                path = Path.Combine(path, p);
            return path;
        }
    }
}
