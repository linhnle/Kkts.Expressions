using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Kkts.Expressions.Internal;

namespace Kkts.Expressions
{
    /// <summary>Builds entity predicates from immutable nested filter trees.</summary>
    public static class FilterTreeExtensions
    {
        public static Expression<Func<T, bool>> BuildPredicate<T>(
            this FilterNode tree,
            VariableResolver variableResolver = null,
            IEnumerable<string> validProperties = null,
            IDictionary<string, string> propertyMapping = null)
        {
            return (Expression<Func<T, bool>>)BuildPredicate(
                tree,
                typeof(T),
                variableResolver,
                validProperties,
                propertyMapping);
        }

        public static LambdaExpression BuildPredicate(
            this FilterNode tree,
            Type type,
            VariableResolver variableResolver = null,
            IEnumerable<string> validProperties = null,
            IDictionary<string, string> propertyMapping = null)
        {
            var result = TryBuildPredicate(tree, type, variableResolver, validProperties, propertyMapping);
            if (result.Succeeded) return result.Result;
            ThrowFailure(result);
            throw new InvalidOperationException("Filter-tree construction failed.");
        }

        public static EvaluationResult<T, bool> TryBuildPredicate<T>(
            this FilterNode tree,
            VariableResolver variableResolver = null,
            IEnumerable<string> validProperties = null,
            IDictionary<string, string> propertyMapping = null)
        {
            var result = TryBuildPredicate(tree, typeof(T), variableResolver, validProperties, propertyMapping);
            return result.ToGeneric<T, bool>();
        }

        public static EvaluationResult TryBuildPredicate(
            this FilterNode tree,
            Type type,
            VariableResolver variableResolver = null,
            IEnumerable<string> validProperties = null,
            IDictionary<string, string> propertyMapping = null)
        {
            ValidateArguments(tree, type);
            var schema = ExpressionSchema.FromType(type, validProperties, propertyMapping);
            var validation = FilterTreeSemanticValidator.Validate(
                tree,
                schema,
                allowUnresolvedVariables: true);
            if (!validation.Succeeded)
            {
                return new EvaluationResult { Diagnostics = validation.Diagnostics };
            }

            try
            {
                return new EvaluationResult
                {
                    Result = FilterTreePredicateBuilder.Build(tree, schema, variableResolver),
                    Succeeded = true
                };
            }
            catch (FilterTreeBuildException exception)
            {
                return new EvaluationResult
                {
                    Exception = exception,
                    Diagnostics = new[] { exception.Diagnostic }
                };
            }
            catch (Exception exception)
            {
                return new EvaluationResult { Exception = exception };
            }
        }

        public static async Task<Expression<Func<T, bool>>> BuildPredicateAsync<T>(
            this FilterNode tree,
            VariableResolver variableResolver = null,
            IEnumerable<string> validProperties = null,
            IDictionary<string, string> propertyMapping = null,
            CancellationToken cancellationToken = default)
        {
            return (Expression<Func<T, bool>>)await BuildPredicateAsync(
                tree,
                typeof(T),
                variableResolver,
                validProperties,
                propertyMapping,
                cancellationToken).ConfigureAwait(false);
        }

        public static async Task<LambdaExpression> BuildPredicateAsync(
            this FilterNode tree,
            Type type,
            VariableResolver variableResolver = null,
            IEnumerable<string> validProperties = null,
            IDictionary<string, string> propertyMapping = null,
            CancellationToken cancellationToken = default)
        {
            var result = await TryBuildPredicateAsync(
                tree,
                type,
                variableResolver,
                validProperties,
                propertyMapping,
                cancellationToken).ConfigureAwait(false);
            if (result.Succeeded) return result.Result;
            ThrowFailure(result);
            throw new InvalidOperationException("Filter-tree construction failed.");
        }

        public static async Task<EvaluationResult<T, bool>> TryBuildPredicateAsync<T>(
            this FilterNode tree,
            VariableResolver variableResolver = null,
            IEnumerable<string> validProperties = null,
            IDictionary<string, string> propertyMapping = null,
            CancellationToken cancellationToken = default)
        {
            var result = await TryBuildPredicateAsync(
                tree,
                typeof(T),
                variableResolver,
                validProperties,
                propertyMapping,
                cancellationToken).ConfigureAwait(false);
            return result.ToGeneric<T, bool>();
        }

        public static async Task<EvaluationResult> TryBuildPredicateAsync(
            this FilterNode tree,
            Type type,
            VariableResolver variableResolver = null,
            IEnumerable<string> validProperties = null,
            IDictionary<string, string> propertyMapping = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateArguments(tree, type);
            var schema = ExpressionSchema.FromType(type, validProperties, propertyMapping);
            var validation = FilterTreeSemanticValidator.Validate(
                tree,
                schema,
                allowUnresolvedVariables: true);
            if (!validation.Succeeded)
                return new EvaluationResult { Diagnostics = validation.Diagnostics };

            try
            {
                var result = await FilterTreePredicateBuilder.BuildAsync(
                    tree,
                    schema,
                    variableResolver,
                    cancellationToken).ConfigureAwait(false);
                return new EvaluationResult { Result = result, Succeeded = true };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (FilterTreeBuildException exception)
            {
                return new EvaluationResult
                {
                    Exception = exception,
                    Diagnostics = new[] { exception.Diagnostic }
                };
            }
            catch (Exception exception)
            {
                return new EvaluationResult { Exception = exception };
            }
        }

        private static void ValidateArguments(FilterNode tree, Type type)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (type == null) throw new ArgumentNullException(nameof(type));
        }

        private static void ThrowFailure(EvaluationResult result)
        {
            if (result.Exception != null)
                ExceptionDispatchInfo.Capture(result.Exception).Throw();
            var message = result.Diagnostics.Count > 0
                ? result.Diagnostics[0].Message
                : "Filter-tree construction failed.";
            throw new InvalidOperationException(message);
        }
    }
}
