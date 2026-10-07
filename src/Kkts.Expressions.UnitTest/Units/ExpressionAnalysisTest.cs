using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionAnalysisTest
    {
        [Fact]
        public void Result_CollectionsAreOwnedReadOnlySnapshots()
        {
            var tokens = new List<ExpressionToken> { new ExpressionToken(ExpressionTokenKind.Property, 0, 2) };
            var diagnostics = new List<ExpressionSyntaxDiagnostic>
            {
                new ExpressionSyntaxDiagnostic("unexpected-token", "Unexpected token.", 2, 1)
            };
            var result = new ExpressionAnalysisResult(tokens, diagnostics, false);
            tokens.Clear();
            diagnostics.Clear();
            Assert.Single(result.Tokens);
            Assert.Single(result.Diagnostics);
            Assert.False(result.IsComplete);
            Assert.Throws<NotSupportedException>(() => ((IList<ExpressionToken>)result.Tokens).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<ExpressionSyntaxDiagnostic>)result.Diagnostics).Clear());
            foreach (var type in new[] { typeof(ExpressionAnalysisResult), typeof(ExpressionToken), typeof(ExpressionSyntaxDiagnostic) })
                Assert.All(type.GetProperties(), property => Assert.False(property.CanWrite));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" \t\r\n ")]
        public void BlankInput_IsAnEmptyEditor(string source)
        {
            var result = Interpreter.AnalyzeExpression(source);
            Assert.False(result.IsComplete);
            Assert.Empty(result.Tokens);
            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void NullInput_IsAnArgumentError()
        {
            Assert.Throws<ArgumentNullException>(() => Interpreter.AnalyzeExpression(null));
        }

        [Fact]
        public void Tokens_UseExactUntrimmedUtf16Spans()
        {
            var result = Interpreter.AnalyzeExpression("  Id = $x  ");
            Assert.True(result.IsComplete);
            Assert.Empty(result.Diagnostics);
            Assert.Equal(new[]
            {
                (ExpressionTokenKind.Property, 2, 2),
                (ExpressionTokenKind.Operator, 5, 1),
                (ExpressionTokenKind.Variable, 7, 2)
            }, Shape(result));

            const string unicode = "Name = '\ud83d\ude00' and Id = 1";
            var unicodeResult = Interpreter.AnalyzeExpression(unicode);
            Assert.True(unicodeResult.IsComplete);
            Assert.Contains(unicodeResult.Tokens, t => t.Kind == ExpressionTokenKind.Constant && t.Start == 7 && t.Length == 4);
            Assert.Contains(unicodeResult.Tokens, t => t.Kind == ExpressionTokenKind.Property && t.Start == 16 && t.Length == 2);
            AssertInvariants(unicode, unicodeResult);
        }

        [Fact]
        public void Tokens_ClassifyPathsFunctionsSignsAndCompoundOperators()
        {
            const string source = "Customer.Name not in [$user.name, 'a']";
            var result = Interpreter.AnalyzeExpression(source);
            Assert.True(result.IsComplete, Messages(result));
            Assert.Equal(new[]
            {
                (ExpressionTokenKind.Property, "Customer.Name"),
                (ExpressionTokenKind.Operator, "not in"),
                (ExpressionTokenKind.Punctuation, "["),
                (ExpressionTokenKind.Variable, "$user.name"),
                (ExpressionTokenKind.Punctuation, ","),
                (ExpressionTokenKind.Constant, "'a'"),
                (ExpressionTokenKind.Punctuation, "]")
            }, TextShape(source, result));

            const string signs = "Id--5 = 9 and Name.contains('a+b')";
            var signResult = Interpreter.AnalyzeExpression(signs);
            Assert.True(signResult.IsComplete, Messages(signResult));
            Assert.Equal(new[] { "-", "=", "and", "contains" },
                signResult.Tokens.Where(t => t.Kind == ExpressionTokenKind.Operator).Select(t => Slice(signs, t)));
            Assert.Contains(signResult.Tokens, t => t.Kind == ExpressionTokenKind.Constant && Slice(signs, t) == "-5");
            Assert.Contains(signResult.Tokens, t => t.Kind == ExpressionTokenKind.Constant && Slice(signs, t) == "'a+b'");
        }

        [Fact]
        public void EverySupportedOperator_HasOneExactSourceToken()
        {
            foreach (var op in Interpreter.ComparisonOperators)
            {
                var source = $"Id {op} " + (Interpreter.IsMembership(op) ? "[1]" : "1");
                var result = Interpreter.AnalyzeExpression(source);
                Assert.True(result.IsComplete, $"{source}: {Messages(result)}");
                var token = result.Tokens.Single(t => t.Start == 3);
                Assert.Equal((ExpressionTokenKind.Operator, 3, op.Length), (token.Kind, token.Start, token.Length));
                Assert.Equal(op, Slice(source, token));
            }
            foreach (var op in new[] { "and", "&&", "&", "or", "||", "|" })
            {
                var source = $"true {op} false";
                var result = Interpreter.AnalyzeExpression(source);
                Assert.True(result.IsComplete, Messages(result));
                var token = result.Tokens.Single(t => t.Start == 5);
                Assert.Equal((ExpressionTokenKind.Operator, 5, op.Length), (token.Kind, token.Start, token.Length));
            }
            foreach (var op in Interpreter.ComparisonFunctionOperators)
            {
                var source = $"Name.{op}('x')";
                var result = Interpreter.AnalyzeExpression(source);
                Assert.True(result.IsComplete, Messages(result));
                var token = result.Tokens.Single(t => t.Start == 5);
                Assert.Equal((ExpressionTokenKind.Operator, 5, op.Length), (token.Kind, token.Start, token.Length));
            }
            foreach (var op in new[] { "+", "-" })
            {
                var result = Interpreter.AnalyzeExpression($"Id {op} -5 = 0");
                Assert.True(result.IsComplete, Messages(result));
                Assert.Contains((ExpressionTokenKind.Operator, 3, 1), Shape(result));
                Assert.Contains((ExpressionTokenKind.Constant, 5, 2), Shape(result));
            }
            Assert.Contains((ExpressionTokenKind.Operator, 0, 3), Shape(Interpreter.AnalyzeExpression("not(false)")));
            Assert.Equal(new[]
            {
                (ExpressionTokenKind.Operator, 0, 1),
                (ExpressionTokenKind.Operator, 1, 1),
                (ExpressionTokenKind.Constant, 2, 5)
            }, Shape(Interpreter.AnalyzeExpression("!!false")));
        }

        [Theory]
        [InlineData("Name = 'a\\'b'")]
        [InlineData("Name = \"a\\\"b\"")]
        public void EscapedQuotedConstants_KeepTheirFullRawRange(string source)
        {
            var result = Interpreter.AnalyzeExpression(source);
            Assert.True(result.IsComplete, Messages(result));
            Assert.Equal(new[]
            {
                (ExpressionTokenKind.Property, 0, 4),
                (ExpressionTokenKind.Operator, 5, 1),
                (ExpressionTokenKind.Constant, 7, 6)
            }, Shape(result));
            AssertInvariants(source, result);
        }

        [Fact]
        public void Paths_AllowExistingWhitespaceAndKeepOneOriginalSpan()
        {
            const string source = "Customer . Name = $user. name";
            var result = Interpreter.AnalyzeExpression(source);
            Assert.True(result.IsComplete, Messages(result));
            Assert.Equal(new[]
            {
                (ExpressionTokenKind.Property, "Customer . Name"),
                (ExpressionTokenKind.Operator, "="),
                (ExpressionTokenKind.Variable, "$user. name")
            }, TextShape(source, result));
            var unfinished = Interpreter.AnalyzeExpression("Customer. \t");
            Assert.Contains(unfinished.Diagnostics, d => d.Start == 11 && d.Length == 0);
        }

        [Theory]
        [InlineData("Id in []")]
        [InlineData("Id in [1, 2, null, $amount]")]
        [InlineData("Id in (1, 2, null, $amount)")]
        [InlineData("Id in {1, 2, null, $amount}")]
        [InlineData("Id not \t\r\n in [1, 2]")]
        [InlineData("Id in $values")]
        [InlineData("Name in ['a,b', \"c\"]")]
        [InlineData(@"Name in [a\], b]")]
        public void Lists_KeepItemsAndDelimitersHighlightable(string source)
        {
            var result = Interpreter.AnalyzeExpression(source);
            Assert.True(result.IsComplete, Messages(result));
            AssertInvariants(source, result);
            Assert.DoesNotContain(result.Tokens, t => t.Kind == ExpressionTokenKind.Constant && Slice(source, t) == source);
        }

        [Theory]
        [InlineData("true", ExpressionTokenKind.Constant)]
        [InlineData("FALSE ", ExpressionTokenKind.Constant)]
        [InlineData("null\t", ExpressionTokenKind.Constant)]
        [InlineData("$user.name", ExpressionTokenKind.Variable)]
        [InlineData("Customer.Name", ExpressionTokenKind.Property)]
        [InlineData("contains", ExpressionTokenKind.Property)]
        [InlineData("12345678901234567890123456789012345678901234567890", ExpressionTokenKind.Constant)]
        [InlineData("-.5", ExpressionTokenKind.Constant)]
        public void StandaloneValues_AreSyntaxOnly(string source, ExpressionTokenKind kind)
        {
            var result = Interpreter.AnalyzeExpression(source);
            Assert.True(result.IsComplete, Messages(result));
            Assert.Empty(result.Diagnostics);
            Assert.Equal(kind, Assert.Single(result.Tokens).Kind);
        }

        [Theory]
        [InlineData("MissingProperty = $missing")]
        [InlineData("1 + 2")]
        [InlineData("Integer - 'text' = null")]
        [InlineData("String = 'not a date'")]
        public void Analysis_DoesNotBindOrEvaluate(string source)
        {
            var result = Interpreter.AnalyzeExpression(source);
            Assert.True(result.IsComplete, Messages(result));
            Assert.Empty(result.Diagnostics);
        }

        [Theory]
        [InlineData("  Id = ", "missing-operand", 7)]
        [InlineData("Name = 'abc", "unterminated-string", 11)]
        [InlineData("Name = \"abc", "unterminated-string", 11)]
        [InlineData("(Id = 1 ", "unmatched-delimiter", 8)]
        [InlineData("Id in [1, 2", "unmatched-delimiter", 11)]
        [InlineData("$", "incomplete-identifier", 1)]
        [InlineData("Id.", "incomplete-identifier", 3)]
        [InlineData("Id +", "missing-operand", 4)]
        public void IncompleteInput_UsesEofCaret(string source, string code, int position)
        {
            var result = Interpreter.AnalyzeExpression(source);
            Assert.False(result.IsComplete);
            Assert.Contains(result.Diagnostics, d => d.Code == code && d.Start == position && d.Length == 0);
            AssertInvariants(source, result);
        }

        [Fact]
        public void UnterminatedString_RetainsItsClassificationAndDoesNotRecoverInsideIt()
        {
            const string source = "Name = 'abc and Id = )";
            var result = Interpreter.AnalyzeExpression(source);
            var token = result.Tokens.Last();
            Assert.Equal((ExpressionTokenKind.Constant, 7, source.Length - 7), (token.Kind, token.Start, token.Length));
            Assert.Single(result.Diagnostics);
            Assert.Equal("unterminated-string", result.Diagnostics[0].Code);
        }

        [Fact]
        public void Recovery_ReportsIndependentErrorsAndHighlightsTheSuffix()
        {
            const string source = "Id = ) and Name = ] and IsEnabled = true";
            var result = Interpreter.AnalyzeExpression(source);
            Assert.False(result.IsComplete);
            Assert.Equal(new[] { ("unexpected-token", 5, 1), ("unexpected-token", 18, 1) },
                result.Diagnostics.Select(d => (d.Code, d.Start, d.Length)).ToArray());
            Assert.Contains(result.Tokens, t => Slice(source, t) == "Name" && t.Kind == ExpressionTokenKind.Property);
            Assert.Contains(result.Tokens, t => Slice(source, t) == "IsEnabled" && t.Kind == ExpressionTokenKind.Property);
            Assert.Contains(result.Tokens, t => Slice(source, t) == "true" && t.Kind == ExpressionTokenKind.Constant);
            AssertInvariants(source, result);
        }

        [Theory]
        [InlineData("Id = 1 # and Name = 'x'")]
        [InlineData("(Id = ] and Name = 'x') and Id = )")]
        [InlineData("Name = 'and ]' and Id = )")]
        [InlineData("Id === 1 and Name = )")]
        [InlineData("Id = 1 + and Name = ]")]
        [InlineData("()")]
        [InlineData("not()")]
        [InlineData("Name.contains()")]
        [InlineData("Id = 1.2.3")]
        public void MalformedInput_ReturnsPositionedDiagnostics(string source)
        {
            var result = Interpreter.AnalyzeExpression(source);
            Assert.False(result.IsComplete);
            Assert.NotEmpty(result.Diagnostics);
            AssertInvariants(source, result);
            if (source.Contains("'and ]'"))
                Assert.Single(result.Diagnostics);
        }

        [Fact]
        public void UnknownCharacters_DoNotHideLaterTokens()
        {
            const string source = "Id = 1 # and Name = 'x'";
            var result = Interpreter.AnalyzeExpression(source);
            Assert.Contains(result.Tokens, t => t.Kind == ExpressionTokenKind.Unknown && t.Start == 7 && t.Length == 1);
            Assert.Contains(result.Diagnostics, d => d.Code == "unknown-text" && d.Start == 7 && d.Length == 1);
            Assert.Contains(result.Tokens, t => Slice(source, t) == "Name");
            Assert.Contains(result.Tokens, t => Slice(source, t) == "'x'");
        }

        [Fact]
        public void ScopeRecovery_SuppressesCascadesButChecksLaterOperands()
        {
            var result = Interpreter.AnalyzeExpression("(Id = #)");
            Assert.Equal("unknown-text", Assert.Single(result.Diagnostics).Code);
            var multiple = Interpreter.AnalyzeExpression("(Id = # and Name = )");
            Assert.Equal(new[] { ("unknown-text", 6, 1), ("missing-operand", 19, 1) },
                multiple.Diagnostics.Select(d => (d.Code, d.Start, d.Length)).ToArray());
        }

        [Theory]
        [InlineData("  Id = $x  ", true)]
        [InlineData("Name = 'abc", false)]
        [InlineData("Id = ) and Name = ] and IsEnabled = true", false)]
        [InlineData("Id = 1 # and Name = '<script>'", false)]
        [InlineData(" \t\r\n ", false)]
        public void ReadmeHighlightHelper_PreservesEverySourceCharacter(string source, bool complete)
        {
            var runs = BuildHighlightRuns(source, out var result);
            Assert.Equal(source, string.Concat(runs.Select(r => r.Text)));
            Assert.Equal(complete, result.IsComplete);
            foreach (var token in result.Tokens)
                Assert.Contains(runs, run => run.Text == Slice(source, token) && run.Style == token.Kind.ToString().ToLowerInvariant());
            AssertInvariants(source, result);
        }

        [Theory]
        [InlineData("tr-TR")]
        [InlineData("fr-FR")]
        public void Keywords_AreCultureIndependentAndSourcePreserving(string cultureName)
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(cultureName);
                const string source = "Id NOT\tIN [1, 2] AND IsEnabled = TRUE";
                var result = Interpreter.AnalyzeExpression(source);
                Assert.True(result.IsComplete, Messages(result));
                Assert.Contains(result.Tokens, t => t.Kind == ExpressionTokenKind.Operator && Slice(source, t) == "NOT\tIN");
                Assert.Contains(result.Tokens, t => t.Kind == ExpressionTokenKind.Constant && Slice(source, t) == "TRUE");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public async Task Snapshots_AreIndependentAcrossEditsAndParallelCalls()
        {
            var inputs = new[] { "Id", "Id =", "Id = 1", "" };
            var results = inputs.Select(Interpreter.AnalyzeExpression).ToArray();
            Assert.Equal(new[] { true, false, true, false }, results.Select(r => r.IsComplete));
            Assert.Empty(results[0].Diagnostics);
            Assert.NotEmpty(results[1].Diagnostics);
            Assert.Empty(results[2].Diagnostics);
            Assert.Empty(results[3].Tokens);
            var parallel = await Task.WhenAll(Enumerable.Range(0, 64).Select(index => Task.Run(() =>
                Interpreter.AnalyzeExpression(inputs[index % inputs.Length]))));
            for (var i = 0; i < parallel.Length; ++i)
            {
                Assert.Equal(Shape(results[i % inputs.Length]), Shape(parallel[i]));
                Assert.Equal(results[i % inputs.Length].IsComplete, parallel[i].IsComplete);
            }
            Assert.Equal((ExpressionTokenKind.Property, 0, 2), Shape(results[0]).Single());
        }

        [Theory]
        [InlineData("Integer = 1")]
        [InlineData("Integer == 1")]
        [InlineData("Integer != 1")]
        [InlineData("Integer <> 1")]
        [InlineData("Integer < 1")]
        [InlineData("Integer <= 1")]
        [InlineData("Integer > 1")]
        [InlineData("Integer >= 1")]
        [InlineData("String contains 'a'")]
        [InlineData("String contain 'a'")]
        [InlineData("String @ 'a'")]
        [InlineData("String startswith 'a'")]
        [InlineData("String startwith 'a'")]
        [InlineData("String @* 'a'")]
        [InlineData("String endswith 'a'")]
        [InlineData("String endwith 'a'")]
        [InlineData("String *@ 'a'")]
        [InlineData("Integer in [1, 2]")]
        [InlineData("Integer not in [1, 2]")]
        [InlineData("Boolean && true")]
        [InlineData("Boolean & true")]
        [InlineData("Boolean and true")]
        [InlineData("Boolean || true")]
        [InlineData("Boolean | true")]
        [InlineData("Boolean or true")]
        [InlineData("!!Boolean")]
        [InlineData("not(Boolean)")]
        [InlineData("(Integer + 2) - 1 = 5 and Boolean = true")]
        [InlineData("String.contains('a')")]
        [InlineData("String.contain('a')")]
        [InlineData("String.startswith('a')")]
        [InlineData("String.startwith('a')")]
        [InlineData("String.endswith('a')")]
        [InlineData("String.endwith('a')")]
        [InlineData("not((Integer = 5 or Integer = 6)) and String.contains(('a' + 'b'))")]
        [InlineData("String = 'a\\'b'")]
        [InlineData("String = \"a\\\"b\"")]
        [InlineData("Integer in (1, 2)")]
        [InlineData("Integer in {1, 2}")]
        public void SupportedGrammar_AgreesWithStrictPredicateParsing(string source)
        {
            var predicate = Interpreter.ParsePredicate<TestEntity>(source);
            Assert.True(predicate.Succeeded, predicate.Exception?.ToString());
            var result = Interpreter.AnalyzeExpression(source);
            Assert.True(result.IsComplete, Messages(result));
            Assert.Empty(result.Diagnostics);
            AssertInvariants(source, result);
        }

        [Fact]
        public void GeneratedMalformedAndLongInputs_TerminateWithValidOutputRanges()
        {
            const string alphabet = "Id$x012-+!=@*#&|.,()[]{}'\"\\ \t\r\n";
            var random = new Random(1949);
            for (var iteration = 0; iteration < 2000; ++iteration)
            {
                var source = new string(Enumerable.Range(0, random.Next(0, 65))
                    .Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
                AssertInvariants(source, Interpreter.AnalyzeExpression(source));
            }
            foreach (var source in new[]
            {
                string.Join(" and ", Enumerable.Repeat("Id = )", 1024)),
                "Name = '" + new string('x', 16384),
                new string('(', 1024) + "Id = 1" + new string(')', 1024),
                "Id = " + new string('9', 16384)
            })
                AssertInvariants(source, Interpreter.AnalyzeExpression(source));
        }

        private static (ExpressionTokenKind, int, int)[] Shape(ExpressionAnalysisResult result) =>
            result.Tokens.Select(t => (t.Kind, t.Start, t.Length)).ToArray();

        private static (ExpressionTokenKind, string)[] TextShape(string source, ExpressionAnalysisResult result) =>
            result.Tokens.Select(t => (t.Kind, Slice(source, t))).ToArray();

        private static string Slice(string source, ExpressionToken token) => source.Substring(token.Start, token.Length);

        private static string Messages(ExpressionAnalysisResult result) =>
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code} at {d.Start}: {d.Message}"));

        public static IReadOnlyList<(string Text, string Style)> BuildHighlightRuns(
            string text, out ExpressionAnalysisResult analysis)
        {
            analysis = Interpreter.AnalyzeExpression(text);
            var runs = new List<(string Text, string Style)>();
            var cursor = 0;
            foreach (var token in analysis.Tokens)
            {
                if (token.Start > cursor)
                    runs.Add((text.Substring(cursor, token.Start - cursor), "plain"));

                string style;
                switch (token.Kind)
                {
                    case ExpressionTokenKind.Property: style = "property"; break;
                    case ExpressionTokenKind.Variable: style = "variable"; break;
                    case ExpressionTokenKind.Operator: style = "operator"; break;
                    case ExpressionTokenKind.Constant: style = "constant"; break;
                    case ExpressionTokenKind.Punctuation: style = "punctuation"; break;
                    default: style = "unknown"; break;
                }
                runs.Add((text.Substring(token.Start, token.Length), style));
                cursor = token.Start + token.Length;
            }
            if (cursor < text.Length)
                runs.Add((text.Substring(cursor), "plain"));
            return runs.AsReadOnly();
        }

        private static void AssertInvariants(string source, ExpressionAnalysisResult result)
        {
            var covered = new bool[source.Length];
            var end = 0;
            foreach (var token in result.Tokens)
            {
                Assert.InRange(token.Start, end, source.Length);
                Assert.InRange(token.Length, 1, source.Length - token.Start);
                end = token.Start + token.Length;
                for (var index = token.Start; index < end; ++index)
                {
                    Assert.False(covered[index]);
                    covered[index] = true;
                }
            }
            for (var index = 0; index < source.Length; ++index)
                if (!char.IsWhiteSpace(source[index])) Assert.True(covered[index], $"Uncovered offset {index} in {source}");
            var previous = -1;
            foreach (var diagnostic in result.Diagnostics)
            {
                Assert.InRange(diagnostic.Start, 0, source.Length);
                Assert.InRange(diagnostic.Length, 0, source.Length - diagnostic.Start);
                Assert.True(diagnostic.Start >= previous);
                Assert.False(string.IsNullOrWhiteSpace(diagnostic.Code));
                Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
                previous = diagnostic.Start;
            }
            Assert.Equal(result.Diagnostics.Count, result.Diagnostics.Select(d => (d.Code, d.Start, d.Length)).Distinct().Count());
            if (result.Diagnostics.Count > 0) Assert.False(result.IsComplete);
        }
    }
}
