using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ParserCacheTest
    {
        [Fact]
        public void TokenText_IsCachedAndInvalidatedByBothAppendMethods()
        {
            var parser = new TokenParser();
            parser.Add('I');
            var original = parser.Result;
            var normalized = parser.NormalizedResult;
            Assert.Same(original, parser.Result);
            Assert.Same(normalized, parser.NormalizedResult);
            Assert.Equal("I", original);
            Assert.Equal("i", normalized);

            parser.Add('N');
            Assert.Equal("IN", parser.Result);
            Assert.Equal("in", parser.NormalizedResult);
            Assert.NotSame(original, parser.Result);
            Assert.NotSame(normalized, parser.NormalizedResult);

            var suffix = new TokenParser();
            suffix.Add('X');
            parser.Add(suffix);
            Assert.Equal("INX", parser.Result);
            Assert.Equal("inx", parser.NormalizedResult);

            var start = GC.GetAllocatedBytesForCurrentThread();
            var length = 0;
            for (var i = 0; i < 100; ++i)
                length += parser.Result.Length + parser.NormalizedResult.Length;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.Equal(600, length);
            Assert.Equal(0, allocated);
        }

        [Fact]
        public void PropertyMetadata_IsSharedReadOnlyAndAllocationFreeOnCacheHits()
        {
            var metadata = PropertyMetadata.GetPropertyNames(typeof(TestEntity));
            Assert.Same(metadata, PropertyMetadata.GetPropertyNames(typeof(TestEntity)));
            Assert.True(metadata.ContainsKey("integer"));
            var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(metadata);
            Assert.True(dictionary.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => dictionary.Add("Injected", "Injected"));

            var expectedCount = CountCachedProperties();
            var start = GC.GetAllocatedBytesForCurrentThread();
            var count = CountCachedProperties();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.Equal(expectedCount, count);
            Assert.Equal(0, allocated);
        }

        [Fact]
        public void PropertyMetadata_ConcurrentReadsShareOneResult()
        {
            var metadata = PropertyMetadata.GetPropertyNames(typeof(TestEntity));
            Parallel.For(0, 32, _ =>
                Assert.Same(metadata, PropertyMetadata.GetPropertyNames(typeof(TestEntity))));
        }

        [Fact]
        public void BuildArguments_KeepWhitelistsNestedPathsAndDiagnosticsLocal()
        {
            var unrestricted = new BuildArgument { EvaluationType = typeof(TestEntity) };
            Assert.True(unrestricted.IsValidProperty("Parent.Id"));
            Assert.False(PropertyMetadata.GetPropertyNames(typeof(TestEntity)).ContainsKey("Parent.Id"));

            var restricted = new BuildArgument
            {
                ValidProperties = new[] { "Integer" },
                EvaluationType = typeof(TestEntity)
            };
            Assert.False(restricted.IsValidProperty("Parent.Id"));
            Assert.Contains("Parent.Id", restricted.InvalidProperties);
            Assert.Empty(unrestricted.InvalidProperties);

            var mapped = new BuildArgument
            {
                EvaluationType = typeof(TestEntity),
                PropertyMapping = new Dictionary<string, string> { ["alias"] = "Integer" }
            };
            Assert.True(mapped.IsValidProperty("alias"));
            var independent = new BuildArgument { EvaluationType = typeof(TestEntity) };
            Assert.False(independent.IsValidProperty("alias"));
            Assert.True(independent.IsValidProperty("Parent.Id"));
        }

        [Fact]
        public void ValidProperties_AreEnumeratedOnce()
        {
            var enumerations = 0;
            IEnumerable<string> Properties()
            {
                ++enumerations;
                yield return "Integer";
            }

            var argument = new BuildArgument
            {
                ValidProperties = Properties(),
                EvaluationType = typeof(TestEntity)
            };
            Assert.Equal(1, enumerations);
            Assert.True(argument.IsValidProperty("Integer"));
            Assert.False(argument.IsValidProperty("String"));
        }

        private static int CountCachedProperties()
        {
            var count = 0;
            for (var i = 0; i < 100; ++i)
                count += PropertyMetadata.GetPropertyNames(typeof(TestEntity)).Count;
            return count;
        }

        private sealed class TokenParser : Parser
        {
            public void Add(char value) => Append(value);
            public void Add(TokenParser parser) => Append(parser);
            public override bool Validate() => true;
            public override bool Accept(char value, int whitespace, int index, ref bool keepTrack, ref bool isStartGroup) => false;
        }
    }
}
