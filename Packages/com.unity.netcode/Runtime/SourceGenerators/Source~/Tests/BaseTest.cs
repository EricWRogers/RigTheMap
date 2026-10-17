using System;
using System.IO;
using NUnit.Framework;

namespace Unity.Netcode.GeneratorTests
{
    class BaseTest
    {
        string? m_OriginalDirectory;

        [SetUp]
        public void SetupCommon()
        {
            LogAssert.StartLogValidation();
            m_OriginalDirectory = Environment.CurrentDirectory;
            //This will point to the com.unity.netcode directory
            string? currentDir = m_OriginalDirectory;
            while (currentDir?.Length > 0 && !currentDir.EndsWith("com.unity.netcode", StringComparison.Ordinal))
                currentDir = Path.GetDirectoryName(currentDir);

            if (currentDir == null || !currentDir.EndsWith("com.unity.netcode", StringComparison.Ordinal))
            {
                Assert.Fail("Cannot find com.unity.netcode folder");
                return;
            }

            //Execute in Runtime/SourceGenerators/Source~/Temp
            Environment.CurrentDirectory = Path.Combine(currentDir, "Runtime", "SourceGenerators", "Source~");
            Generators.Profiler.Initialize();
        }

        [TearDown]
        public void TearDownCommon()
        {
            Environment.CurrentDirectory = m_OriginalDirectory ?? string.Empty;

            LogAssert.ValidateAllLogsWereHandled();
            LogAssert.ResetDebugLogger();
        }
    }

    /// <summary>
    /// Deletes the temp folder and generated files created in <see cref="GeneratorTestHelpers.OutputFolder"/>.
    /// Using a SetUpFixture to ensure this code runs for all tests in this namespace, even if they don't inherit from <see cref="BaseTest"/>
    /// </summary>
    [SetUpFixture]
    class TestOutputCleanupFixture
    {
        [OneTimeTearDown]
        public void DeleteTestOutput()
        {
            try
            {
                if (Directory.Exists(GeneratorTestHelpers.OutputFolder))
                {
                    Directory.Delete(GeneratorTestHelpers.OutputFolder, true);
                }
            }
            catch (Exception e)
            {
                // Log a message rather than failing the run if the delete fails.
                TestContext.Progress.WriteLine($"Unable to delete '{GeneratorTestHelpers.OutputFolder}': {e.Message}");
            }
        }
    }
}
