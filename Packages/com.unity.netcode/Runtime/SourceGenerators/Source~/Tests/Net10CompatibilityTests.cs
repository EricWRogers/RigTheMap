using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Unity.Netcode.Generators;

namespace Unity.Netcode.GeneratorTests
{
    /// <summary>
    /// Covers what changes when a project compiles for net5.0 or later with C# 11 or later, as the MSBuild pipeline
    /// does when it targets net10.0.
    /// </summary>
    [TestFixture]
    class Net10CompatibilityTests : BaseTest
    {
        [TestCase("ref")]
        [TestCase("refint")]
        public void GetImplementationPath_ReferenceAssembly_ReturnsImplementationNextToRefFolder(string refFolder)
        {
            var root = GeneratorTestHelpers.CreateTempProjectDirectory();
            try
            {
                var implementation = Path.Combine(root, "Unity.NetCode.dll");
                var reference = Path.Combine(root, refFolder, "Unity.NetCode.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(reference)!);
                File.WriteAllText(implementation, string.Empty);
                File.WriteAllText(reference, string.Empty);

                Assert.AreEqual(implementation, UserDefinedTemplateRegistryParser.GetImplementationPath(reference),
                    "Reference assemblies cannot be loaded for execution, so the implementation must be used.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void GetImplementationPath_ReferenceAssemblyWithoutImplementation_ReturnsSamePath()
        {
            var root = GeneratorTestHelpers.CreateTempProjectDirectory();
            try
            {
                var reference = Path.Combine(root, "ref", "Unity.NetCode.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(reference)!);
                File.WriteAllText(reference, string.Empty);

                Assert.AreEqual(reference, UserDefinedTemplateRegistryParser.GetImplementationPath(reference));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void GetImplementationPath_ImplementationAssembly_ReturnsSamePath()
        {
            var path = Path.Combine(Path.GetTempPath(), "Library", "ScriptAssemblies", "Unity.NetCode.dll");

            Assert.AreEqual(path, UserDefinedTemplateRegistryParser.GetImplementationPath(path));
        }

        const string k_FixedListGhostSource = @"
            using Unity.Collections;
            using Unity.Entities;
            using Unity.Netcode;

            namespace RefSafetyTests
            {
                public struct FixedListComponent : IComponentData
                {
                    [GhostField] public FixedList32Bytes<int> Values;
                }
            }
            ";

        // Same signatures as the runtime helper. The compiler checks ref safety at the call site from the signature only.
        const string k_FixedArraySerializationUtilsStub = @"
            using System;

            namespace Unity.Netcode.LowLevel.Unsafe
            {
                public static class FixedArraySerializationUtils
                {
                    public static Span<TElement> ToSpan<TContainer, TElement>(ref TContainer container, int length)
                        where TContainer : unmanaged
                        where TElement : unmanaged => default;

                    public static ReadOnlySpan<TElement> ToReadOnlySpan<TContainer, TElement>(ref TContainer container, int length)
                        where TContainer : unmanaged
                        where TElement : unmanaged => default;
                }
            }
            ";

        [Test]
        public void FixedListContainer_CSharp11WithNet7Symbols_HasNoRefSafetyErrors()
        {
            var results = GeneratorTestHelpers.RunGenerators(CSharpSyntaxTree.ParseText(k_FixedListGhostSource));
            var containerSources = results.GeneratedSources
                .Where(s => s.SourceText.ToString().Contains("ToReadOnlySpan()"))
                .ToArray();
            Assert.IsNotEmpty(containerSources, "Expected a generated fixed list container with ToSpan and ToReadOnlySpan.");

            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11, preprocessorSymbols: ["NET7_0_OR_GREATER"]);
            var trees = containerSources
                .Select(s => CSharpSyntaxTree.ParseText(s.SourceText, parseOptions))
                .Append(CSharpSyntaxTree.ParseText(k_FixedArraySerializationUtilsStub, parseOptions));
            var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var compilation = CSharpCompilation.Create("RefSafetyTests", trees,
                [
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
                ],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

            // CS8170: struct members cannot return 'this' by reference. CS8347: a span result may expose 'this'.
            var refSafetyErrors = compilation.GetDiagnostics()
                .Where(d => d.Id == "CS8170" || d.Id == "CS8347")
                .ToArray();
            Assert.IsEmpty(refSafetyErrors, string.Join("\n", refSafetyErrors.Select(d => d.ToString())));
        }
    }
}
