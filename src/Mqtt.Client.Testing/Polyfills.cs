// Copyright (c) 2026 marcschier. Licensed under the MIT License.

#if NETSTANDARD2_0 || NETSTANDARD2_1
// Polyfills for language features that require attributes only present in newer runtimes,
// but that the C# compiler is happy to consume from any assembly when targeting older TFMs.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit;

    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field
            | AttributeTargets.Property,
        Inherited = false,
        AllowMultiple = false)]
    internal sealed class RequiredMemberAttribute : Attribute;

    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) { FeatureName = featureName; }
        public string FeatureName { get; }
        public bool IsOptional { get; init; }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute;
}
#endif
