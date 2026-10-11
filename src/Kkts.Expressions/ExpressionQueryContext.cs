using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Kkts.Expressions.Internal;

namespace Kkts.Expressions
{
    /// <summary>Combines entity metadata and an opt-in query policy for query operations.</summary>
    public sealed class ExpressionQueryContext
    {
        private readonly IReadOnlyList<string> _validProperties;
        private readonly IReadOnlyDictionary<string, IReadOnlyCollection<ComparisonOperator>> _allowedOperators;

        public ExpressionQueryContext(
            ExpressionSchema schema,
            QueryPolicy policy,
            IEnumerable<string> validProperties = null)
        {
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _validProperties = SnapshotValidProperties(validProperties);
            ValidatePublicSchemaRestrictions(schema, policy, _validProperties);
            _allowedOperators = BindAllowedOperators(schema, policy);
            PublicFields = Array.AsReadOnly(schema.Fields.Select(field =>
            {
                var canFilter = IsPropertyQueryable(field.Name);
                var allowedOperators = !canFilter
                    ? Array.Empty<ComparisonOperator>()
                    : TryGetAllowedOperators(field.Name, out var effectiveOperators)
                        ? effectiveOperators
                        : null;
                return new ExpressionFieldDefinition(
                    field.Name,
                    field.ClrType,
                    field.Nullability,
                    canFilter,
                    IsPropertySortable(field.Name),
                    allowedOperators,
                    field.DisplayName,
                    field.Description);
            }).ToArray());
        }

        /// <summary>The immutable entity schema used by this context.</summary>
        public ExpressionSchema Schema { get; }

        /// <summary>The immutable policy used by this context.</summary>
        public QueryPolicy Policy { get; }

        internal IReadOnlyList<string> CompletionValidProperties => _validProperties;

        /// <summary>Returns cursor-local metadata completions using this context's schema, policy, and field restrictions.</summary>
        public ExpressionCompletionResult CompleteExpression(
            string expression,
            int cursorPosition,
            ExpressionVariableSchema variables = null,
            ExpressionValueSuggestionSchema valueSuggestions = null,
            ExpressionCompletionOptions options = null) =>
            ExpressionCompleter.Complete(expression, cursorPosition, Schema, variables, valueSuggestions, options, this);

        /// <summary>The additional external-name allowlist, if one was supplied.</summary>
        public IReadOnlyList<string> ValidProperties => _validProperties;

        /// <summary>
        /// The public schema fields with this context's effective permissions and operators.
        /// Empty for legacy schemas.
        /// </summary>
        public IReadOnlyList<ExpressionFieldDefinition> PublicFields { get; }

        /// <summary>Analyzes an expression using the entity metadata and configured policy.</summary>
        public ExpressionSemanticAnalysisResult AnalyzeExpression(
            string expression,
            ExpressionVariableSchema variables = null)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));

            var execution = new QueryPolicyExecution(Policy);
            if (!QueryPolicySourceScanner.TryScan(expression, execution))
            {
                var incompleteSyntax = new ExpressionAnalysisResult(
                    Array.Empty<ExpressionToken>(),
                    Array.Empty<ExpressionSyntaxDiagnostic>(),
                    false);
                return new ExpressionSemanticAnalysisResult(
                    incompleteSyntax,
                    execution.Diagnostics.ToReadOnlyList(),
                    false,
                    isTruncated: true,
                    capDiagnostics: true,
                    atomicConditionCount: execution.ConditionCount.Value);
            }

            var syntaxAnalyzer = new ExpressionAnalyzer(expression, policyAware: true);
            var syntax = syntaxAnalyzer.Analyze();
            return new ExpressionSemanticAnalyzer(
                expression,
                Schema,
                variables,
                syntax,
                this,
                execution,
                syntaxAnalyzer.IsTruncated).Analyze();
        }

        /// <summary>Parses a supported expression into a tree under this context's policy.</summary>
        public FilterOperationResult<FilterNode> ParseFilterTree(
            string expression,
            ExpressionVariableSchema variables = null)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            var sourceExecution = new QueryPolicyExecution(Policy);
            if (!QueryPolicySourceScanner.TryScan(expression, sourceExecution))
                return FilterOperationResult<FilterNode>.Failure(
                    sourceExecution.Diagnostics.ToReadOnlyList(),
                    sourceExecution.Diagnostics.IsTruncated);

            var parsed = FilterExpression.Parse(expression, Schema, variables);
            if (!parsed.Succeeded) return parsed;

            var treeExecution = new QueryPolicyExecution(Policy);
            if (!FilterTreePolicyScanner.TryScan(parsed.Result, this, treeExecution))
                return FilterOperationResult<FilterNode>.Failure(
                    treeExecution.Diagnostics.ToReadOnlyList(),
                    treeExecution.Diagnostics.IsTruncated);
            return parsed;
        }

        /// <summary>Formats a tree under this context's independent tree and expression policies.</summary>
        public FilterOperationResult<string> FormatFilterTree(
            FilterNode tree,
            ExpressionVariableSchema variables = null)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            var treeExecution = new QueryPolicyExecution(Policy);
            if (!FilterTreePolicyScanner.TryScan(tree, this, treeExecution))
                return FilterOperationResult<string>.Failure(
                    treeExecution.Diagnostics.ToReadOnlyList(),
                    treeExecution.Diagnostics.IsTruncated);

            var formatted = FilterExpression.Format(tree, Schema, variables);
            if (!formatted.Succeeded) return formatted;

            var textExecution = new QueryPolicyExecution(Policy);
            if (!QueryPolicySourceScanner.TryScan(formatted.Result, textExecution))
                return FilterOperationResult<string>.Failure(
                    textExecution.Diagnostics.ToReadOnlyList(),
                    textExecution.Diagnostics.IsTruncated);
            return formatted;
        }

        /// <summary>Builds a predicate after enforcing the configured policy on the actual input.</summary>
        public EvaluationResult<T, bool> ParsePredicate<T>(
            string expression,
            VariableResolver variableResolver = null)
        {
            ValidateEntityType(typeof(T));
            return ParsePredicate(expression, variableResolver).ToGeneric<T, bool>();
        }

        /// <summary>Builds a predicate using the entity type bound to this context.</summary>
        public EvaluationResult ParsePredicate(
            string expression,
            VariableResolver variableResolver = null)
        {
            ValidateExpressionArgument(expression);
            if (TryGetRuntimePolicyDiagnostics(expression, out var diagnostics))
                return FailedPolicyResult(diagnostics);

            return ExpressionParser.Parse(
                expression,
                Schema.EntityType,
                CreateBuildArgument(expression, variableResolver));
        }

        /// <summary>Asynchronously builds a policy-checked predicate.</summary>
        public async Task<EvaluationResult<T, bool>> ParsePredicateAsync<T>(
            string expression,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            ValidateEntityType(typeof(T));
            return (await ParsePredicateAsync(expression, variableResolver, cancellationToken)
                .ConfigureAwait(false)).ToGeneric<T, bool>();
        }

        /// <summary>Asynchronously builds a predicate using the entity type bound to this context.</summary>
        public Task<EvaluationResult> ParsePredicateAsync(
            string expression,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateExpressionArgument(expression);
            if (TryGetRuntimePolicyDiagnostics(expression, out var diagnostics))
                return Task.FromResult(FailedPolicyResult(diagnostics));

            var argument = CreateBuildArgument(expression, variableResolver);
            argument.CancellationToken = cancellationToken;
            return ExpressionParser.ParseAsync(expression, Schema.EntityType, argument);
        }

        /// <summary>Validates a nested filter tree using schema metadata without resolving runtime values.</summary>
        public FilterOperationResult<FilterNode> ValidateFilterTree(
            FilterNode tree,
            ExpressionVariableSchema variables = null)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            return ValidateFilterTree(tree, variables, allowUnresolvedVariables: false, new QueryPolicyExecution(Policy));
        }

        /// <summary>Builds a policy-checked predicate directly from a nested filter tree.</summary>
        public Expression<Func<T, bool>> BuildPredicate<T>(
            FilterNode tree,
            VariableResolver variableResolver = null)
        {
            ValidateEntityType(typeof(T));
            return ThrowIfFailed(TryBuildPredicate<T>(tree, variableResolver));
        }

        /// <summary>Builds a policy-checked predicate using this context's entity type.</summary>
        public LambdaExpression BuildPredicate(
            FilterNode tree,
            VariableResolver variableResolver = null)
        {
            return ThrowIfFailed(TryBuildPredicate(tree, variableResolver));
        }

        /// <summary>Attempts policy-checked nested-tree construction without returning a partial predicate.</summary>
        public EvaluationResult<T, bool> TryBuildPredicate<T>(
            FilterNode tree,
            VariableResolver variableResolver = null)
        {
            ValidateEntityType(typeof(T));
            return TryBuildPredicate(tree, variableResolver).ToGeneric<T, bool>();
        }

        /// <summary>Attempts nested-tree construction using this context's entity type.</summary>
        public EvaluationResult TryBuildPredicate(
            FilterNode tree,
            VariableResolver variableResolver = null)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            var execution = new QueryPolicyExecution(Policy);
            var validation = ValidateFilterTree(
                tree,
                variables: null,
                allowUnresolvedVariables: true,
                execution);
            if (!validation.Succeeded)
                return FailureFromFilterTreeValidation(validation);

            try
            {
                var argument = CreateBuildArgument(string.Empty, variableResolver, execution);
                return new EvaluationResult
                {
                    Result = FilterTreePredicateBuilder.Build(tree, Schema, variableResolver, argument),
                    Succeeded = true
                };
            }
            catch (QueryPolicyException exception)
            {
                return FailedPolicyResult(exception.Diagnostics);
            }
            catch (FilterTreeBuildException exception)
            {
                if (exception.InnerException is QueryPolicyException policyException)
                    return FailedPolicyResult(policyException.Diagnostics);
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

        /// <summary>Asynchronously builds a policy-checked nested-tree predicate.</summary>
        public async Task<Expression<Func<T, bool>>> BuildPredicateAsync<T>(
            FilterNode tree,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            ValidateEntityType(typeof(T));
            return ThrowIfFailed(await TryBuildPredicateAsync<T>(
                tree,
                variableResolver,
                cancellationToken).ConfigureAwait(false));
        }

        /// <summary>Asynchronously builds a nested-tree predicate using this context's entity type.</summary>
        public async Task<LambdaExpression> BuildPredicateAsync(
            FilterNode tree,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            return ThrowIfFailed(await TryBuildPredicateAsync(
                tree,
                variableResolver,
                cancellationToken).ConfigureAwait(false));
        }

        /// <summary>Asynchronously attempts tree construction without returning a partial predicate.</summary>
        public async Task<EvaluationResult<T, bool>> TryBuildPredicateAsync<T>(
            FilterNode tree,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            ValidateEntityType(typeof(T));
            return (await TryBuildPredicateAsync(
                tree,
                variableResolver,
                cancellationToken).ConfigureAwait(false)).ToGeneric<T, bool>();
        }

        /// <summary>Asynchronously attempts tree construction using this context's entity type.</summary>
        public async Task<EvaluationResult> TryBuildPredicateAsync(
            FilterNode tree,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            var execution = new QueryPolicyExecution(Policy);
            var validation = ValidateFilterTree(
                tree,
                variables: null,
                allowUnresolvedVariables: true,
                execution);
            if (!validation.Succeeded)
                return FailureFromFilterTreeValidation(validation);

            try
            {
                var argument = CreateBuildArgument(string.Empty, variableResolver, execution);
                argument.CancellationToken = cancellationToken;
                var result = await FilterTreePredicateBuilder.BuildAsync(
                    tree,
                    Schema,
                    variableResolver,
                    cancellationToken,
                    argument).ConfigureAwait(false);
                return new EvaluationResult { Result = result, Succeeded = true };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (QueryPolicyException exception)
            {
                return FailedPolicyResult(exception.Diagnostics);
            }
            catch (FilterTreeBuildException exception)
            {
                if (exception.InnerException is QueryPolicyException policyException)
                    return FailedPolicyResult(policyException.Diagnostics);
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

        /// <summary>Builds a direct field predicate and throws if construction or policy validation fails.</summary>
        public Expression<Func<T, bool>> BuildPredicate<T>(
            string propertyName,
            ComparisonOperator comparisonOperator,
            object value,
            VariableResolver variableResolver = null)
        {
            ValidateEntityType(typeof(T));
            var result = TryBuildPredicate<T>(propertyName, comparisonOperator, value, variableResolver);
            return ThrowIfFailed(result);
        }

        /// <summary>Builds a direct field predicate using the schema entity type.</summary>
        public LambdaExpression BuildPredicate(
            string propertyName,
            ComparisonOperator comparisonOperator,
            object value,
            VariableResolver variableResolver = null)
        {
            var result = TryBuildPredicate(propertyName, comparisonOperator, value, variableResolver);
            return ThrowIfFailed(result);
        }

        /// <summary>Attempts a direct field predicate construction without returning a predicate on failure.</summary>
        public EvaluationResult<T, bool> TryBuildPredicate<T>(
            string propertyName,
            ComparisonOperator comparisonOperator,
            object value,
            VariableResolver variableResolver = null)
        {
            ValidateEntityType(typeof(T));
            return TryBuildPredicate(propertyName, comparisonOperator, value, variableResolver)
                .ToGeneric<T, bool>();
        }

        /// <summary>Attempts a direct field predicate construction using the schema entity type.</summary>
        public EvaluationResult TryBuildPredicate(
            string propertyName,
            ComparisonOperator comparisonOperator,
            object value,
            VariableResolver variableResolver = null)
        {
            ValidateDirectArguments(propertyName, comparisonOperator);
            if (TryGetDirectPolicyDiagnostics(propertyName, comparisonOperator, value, out var diagnostics))
                return FailedPolicyResult(diagnostics);

            try
            {
                var clrPath = Schema.TryGetPolicyPathInfo(propertyName, out var mappedPath, out _, out _)
                    ? mappedPath
                    : propertyName;
                var argument = CreateBuildArgument(string.Empty, variableResolver);
                if (Interpreter.IsMembership(comparisonOperator))
                    value = argument.SnapshotDirectMembershipCollection(value, GetPropertyType(clrPath));
                var predicate = Interpreter.BuildPredicate(
                    comparisonOperator,
                    clrPath,
                    value,
                    Schema.EntityType,
                    argument);
                return new EvaluationResult
                {
                    Result = predicate,
                    Succeeded = true
                };
            }
            catch (QueryPolicyException exception)
            {
                return FailedPolicyResult(exception.Diagnostics);
            }
            catch (Exception exception)
            {
                return new EvaluationResult { Exception = exception };
            }
        }

        /// <summary>Attempts to build a policy-checked predicate from one structured filter.</summary>
        public EvaluationResult<T, bool> TryBuildPredicate<T>(
            Filter filter,
            VariableResolver variableResolver = null)
        {
            ValidateEntityType(typeof(T));
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            return TryBuildStructuredPredicate(
                new[] { filter },
                null,
                variableResolver,
                "Filter").ToGeneric<T, bool>();
        }

        /// <summary>Attempts to build a policy-checked predicate from structured filters combined with AND.</summary>
        public EvaluationResult<T, bool> TryBuildPredicate<T>(
            IEnumerable<Filter> filters,
            VariableResolver variableResolver = null)
        {
            ValidateEntityType(typeof(T));
            if (filters == null) throw new ArgumentNullException(nameof(filters));
            return TryBuildStructuredPredicate(filters, null, variableResolver, "Filters").ToGeneric<T, bool>();
        }

        /// <summary>Attempts to build a policy-checked predicate from a structured filter group.</summary>
        public EvaluationResult<T, bool> TryBuildPredicate<T>(
            FilterGroup filterGroup,
            VariableResolver variableResolver = null)
        {
            ValidateEntityType(typeof(T));
            if (filterGroup == null) throw new ArgumentNullException(nameof(filterGroup));
            return TryBuildStructuredPredicate(null, new[] { filterGroup }, variableResolver, "FilterGroup").ToGeneric<T, bool>();
        }

        /// <summary>Attempts to build a policy-checked predicate from filter groups combined with OR.</summary>
        public EvaluationResult<T, bool> TryBuildPredicate<T>(
            IEnumerable<FilterGroup> filterGroups,
            VariableResolver variableResolver = null)
        {
            ValidateEntityType(typeof(T));
            if (filterGroups == null) throw new ArgumentNullException(nameof(filterGroups));
            return TryBuildStructuredPredicate(null, filterGroups, variableResolver, "FilterGroups").ToGeneric<T, bool>();
        }

        /// <summary>Attempts to build a policy-checked predicate from one structured filter.</summary>
        public EvaluationResult TryBuildPredicate(Filter filter, VariableResolver variableResolver = null)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            return TryBuildStructuredPredicate(new[] { filter }, null, variableResolver, "Filter");
        }

        /// <summary>Attempts to build a policy-checked predicate from structured filters combined with AND.</summary>
        public EvaluationResult TryBuildPredicate(IEnumerable<Filter> filters, VariableResolver variableResolver = null)
        {
            if (filters == null) throw new ArgumentNullException(nameof(filters));
            return TryBuildStructuredPredicate(filters, null, variableResolver, "Filters");
        }

        /// <summary>Attempts to build a policy-checked predicate from a structured filter group.</summary>
        public EvaluationResult TryBuildPredicate(FilterGroup filterGroup, VariableResolver variableResolver = null)
        {
            if (filterGroup == null) throw new ArgumentNullException(nameof(filterGroup));
            return TryBuildStructuredPredicate(null, new[] { filterGroup }, variableResolver, "FilterGroup");
        }

        /// <summary>Attempts to build a policy-checked predicate from filter groups combined with OR.</summary>
        public EvaluationResult TryBuildPredicate(IEnumerable<FilterGroup> filterGroups, VariableResolver variableResolver = null)
        {
            if (filterGroups == null) throw new ArgumentNullException(nameof(filterGroups));
            return TryBuildStructuredPredicate(null, filterGroups, variableResolver, "FilterGroups");
        }

        /// <summary>Asynchronously attempts to build a policy-checked predicate from one structured filter.</summary>
        public async Task<EvaluationResult<T, bool>> TryBuildPredicateAsync<T>(
            Filter filter,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            ValidateEntityType(typeof(T));
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            return (await TryBuildStructuredPredicateAsync(
                new[] { filter },
                null,
                variableResolver,
                "Filter",
                cancellationToken).ConfigureAwait(false)).ToGeneric<T, bool>();
        }

        /// <summary>Asynchronously attempts to build a policy-checked predicate from structured filters.</summary>
        public async Task<EvaluationResult<T, bool>> TryBuildPredicateAsync<T>(
            IEnumerable<Filter> filters,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            ValidateEntityType(typeof(T));
            if (filters == null) throw new ArgumentNullException(nameof(filters));
            return (await TryBuildStructuredPredicateAsync(
                filters,
                null,
                variableResolver,
                "Filters",
                cancellationToken).ConfigureAwait(false)).ToGeneric<T, bool>();
        }

        /// <summary>Asynchronously attempts to build a policy-checked predicate from one filter group.</summary>
        public async Task<EvaluationResult<T, bool>> TryBuildPredicateAsync<T>(
            FilterGroup filterGroup,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            ValidateEntityType(typeof(T));
            if (filterGroup == null) throw new ArgumentNullException(nameof(filterGroup));
            return (await TryBuildStructuredPredicateAsync(
                null,
                new[] { filterGroup },
                variableResolver,
                "FilterGroup",
                cancellationToken).ConfigureAwait(false)).ToGeneric<T, bool>();
        }

        /// <summary>Asynchronously attempts to build a policy-checked predicate from filter groups.</summary>
        public async Task<EvaluationResult<T, bool>> TryBuildPredicateAsync<T>(
            IEnumerable<FilterGroup> filterGroups,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            ValidateEntityType(typeof(T));
            if (filterGroups == null) throw new ArgumentNullException(nameof(filterGroups));
            return (await TryBuildStructuredPredicateAsync(
                null,
                filterGroups,
                variableResolver,
                "FilterGroups",
                cancellationToken).ConfigureAwait(false)).ToGeneric<T, bool>();
        }

        /// <summary>Asynchronously attempts to build a policy-checked predicate from one structured filter.</summary>
        public Task<EvaluationResult> TryBuildPredicateAsync(
            Filter filter,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            return TryBuildStructuredPredicateAsync(new[] { filter }, null, variableResolver, "Filter", cancellationToken);
        }

        /// <summary>Asynchronously attempts to build a policy-checked predicate from structured filters.</summary>
        public Task<EvaluationResult> TryBuildPredicateAsync(
            IEnumerable<Filter> filters,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            if (filters == null) throw new ArgumentNullException(nameof(filters));
            return TryBuildStructuredPredicateAsync(filters, null, variableResolver, "Filters", cancellationToken);
        }

        /// <summary>Asynchronously attempts to build a policy-checked predicate from one filter group.</summary>
        public Task<EvaluationResult> TryBuildPredicateAsync(
            FilterGroup filterGroup,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            if (filterGroup == null) throw new ArgumentNullException(nameof(filterGroup));
            return TryBuildStructuredPredicateAsync(null, new[] { filterGroup }, variableResolver, "FilterGroup", cancellationToken);
        }

        /// <summary>Asynchronously attempts to build a policy-checked predicate from filter groups.</summary>
        public Task<EvaluationResult> TryBuildPredicateAsync(
            IEnumerable<FilterGroup> filterGroups,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            if (filterGroups == null) throw new ArgumentNullException(nameof(filterGroups));
            return TryBuildStructuredPredicateAsync(null, filterGroups, variableResolver, "FilterGroups", cancellationToken);
        }

        public Expression<Func<T, bool>> BuildPredicate<T>(Filter filter, VariableResolver variableResolver = null)
            => ThrowIfFailed(TryBuildPredicate<T>(filter, variableResolver));

        public Expression<Func<T, bool>> BuildPredicate<T>(IEnumerable<Filter> filters, VariableResolver variableResolver = null)
            => ThrowIfFailed(TryBuildPredicate<T>(filters, variableResolver));

        public Expression<Func<T, bool>> BuildPredicate<T>(FilterGroup filterGroup, VariableResolver variableResolver = null)
            => ThrowIfFailed(TryBuildPredicate<T>(filterGroup, variableResolver));

        public Expression<Func<T, bool>> BuildPredicate<T>(IEnumerable<FilterGroup> filterGroups, VariableResolver variableResolver = null)
            => ThrowIfFailed(TryBuildPredicate<T>(filterGroups, variableResolver));

        public LambdaExpression BuildPredicate(Filter filter, VariableResolver variableResolver = null)
            => ThrowIfFailed(TryBuildPredicate(filter, variableResolver));

        public LambdaExpression BuildPredicate(IEnumerable<Filter> filters, VariableResolver variableResolver = null)
            => ThrowIfFailed(TryBuildPredicate(filters, variableResolver));

        public LambdaExpression BuildPredicate(FilterGroup filterGroup, VariableResolver variableResolver = null)
            => ThrowIfFailed(TryBuildPredicate(filterGroup, variableResolver));

        public LambdaExpression BuildPredicate(IEnumerable<FilterGroup> filterGroups, VariableResolver variableResolver = null)
            => ThrowIfFailed(TryBuildPredicate(filterGroups, variableResolver));

        public async Task<Expression<Func<T, bool>>> BuildPredicateAsync<T>(
            Filter filter,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
            => ThrowIfFailed(await TryBuildPredicateAsync<T>(filter, variableResolver, cancellationToken).ConfigureAwait(false));

        public async Task<Expression<Func<T, bool>>> BuildPredicateAsync<T>(
            IEnumerable<Filter> filters,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
            => ThrowIfFailed(await TryBuildPredicateAsync<T>(filters, variableResolver, cancellationToken).ConfigureAwait(false));

        public async Task<Expression<Func<T, bool>>> BuildPredicateAsync<T>(
            FilterGroup filterGroup,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
            => ThrowIfFailed(await TryBuildPredicateAsync<T>(filterGroup, variableResolver, cancellationToken).ConfigureAwait(false));

        public async Task<Expression<Func<T, bool>>> BuildPredicateAsync<T>(
            IEnumerable<FilterGroup> filterGroups,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
            => ThrowIfFailed(await TryBuildPredicateAsync<T>(filterGroups, variableResolver, cancellationToken).ConfigureAwait(false));

        public async Task<LambdaExpression> BuildPredicateAsync(
            Filter filter,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
            => ThrowIfFailed(await TryBuildPredicateAsync(filter, variableResolver, cancellationToken).ConfigureAwait(false));

        public async Task<LambdaExpression> BuildPredicateAsync(
            IEnumerable<Filter> filters,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
            => ThrowIfFailed(await TryBuildPredicateAsync(filters, variableResolver, cancellationToken).ConfigureAwait(false));

        public async Task<LambdaExpression> BuildPredicateAsync(
            FilterGroup filterGroup,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
            => ThrowIfFailed(await TryBuildPredicateAsync(filterGroup, variableResolver, cancellationToken).ConfigureAwait(false));

        public async Task<LambdaExpression> BuildPredicateAsync(
            IEnumerable<FilterGroup> filterGroups,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
            => ThrowIfFailed(await TryBuildPredicateAsync(filterGroups, variableResolver, cancellationToken).ConfigureAwait(false));

        /// <summary>Builds a condition through this context so all filter surfaces share one policy.</summary>
        public Condition BuildCondition(ConditionOptions options, VariableResolver variableResolver = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            return BuildConditionCore(options, variableResolver);
        }

        /// <summary>Builds a typed condition through this context.</summary>
        public Condition<T> BuildCondition<T>(ConditionOptions options, VariableResolver variableResolver = null)
        {
            ValidateEntityType(typeof(T));
            return (Condition<T>)BuildCondition(options, variableResolver);
        }

        /// <summary>Asynchronously builds a condition through this context.</summary>
        public Task<Condition> BuildConditionAsync(
            ConditionOptions options,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            return BuildConditionCoreAsync(options, variableResolver, cancellationToken);
        }

        /// <summary>Asynchronously builds a typed condition through this context.</summary>
        public async Task<Condition<T>> BuildConditionAsync<T>(
            ConditionOptions options,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            ValidateEntityType(typeof(T));
            return (Condition<T>)await BuildConditionAsync(
                options,
                variableResolver,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Builds an ordering clause after applying field permissions and the ordering length budget.</summary>
        public EvaluationResult<OrderByClause> TryBuildOrderByClause(string expression)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            var execution = new QueryPolicyExecution(Policy);
            if (!execution.TryAdmitExpression(expression, "OrderBy"))
                return FailedOrderByResult(execution.Diagnostics.ToReadOnlyList());

            foreach (var segment in expression.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = segment.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                if (!AdmitOrderingProperty(parts[0], "OrderBy", execution)) break;
            }
            if (execution.Diagnostics.ToReadOnlyList().Count > 0)
                return FailedOrderByResult(execution.Diagnostics.ToReadOnlyList());

            try
            {
                return OrderByParser.Parse(expression, CreateBuildArgument(string.Empty, null));
            }
            catch (QueryPolicyException exception)
            {
                return FailedOrderByResult(exception.Diagnostics);
            }
        }

        /// <summary>Builds an ordering clause from a structured order key.</summary>
        public EvaluationResult<OrderByClause> TryBuildOrderByClause(OrderByInfo orderByInfo)
        {
            if (orderByInfo == null) throw new ArgumentNullException(nameof(orderByInfo));
            return TryBuildOrderByClause(new[] { orderByInfo });
        }

        /// <summary>Builds an ordering clause from structured order keys.</summary>
        public EvaluationResult<OrderByClause> TryBuildOrderByClause(IEnumerable<OrderByInfo> orderByInfos)
        {
            if (orderByInfos == null) throw new ArgumentNullException(nameof(orderByInfos));
            var execution = new QueryPolicyExecution(Policy);
            var keys = new List<OrderByInfo>();
            long totalLength = 0;
            var index = 0;
            foreach (var info in orderByInfos)
            {
                if (info == null)
                    return new EvaluationResult<OrderByClause>
                    {
                        Exception = new ArgumentException("An order key cannot be null.")
                    };
                totalLength += info.Property?.Length ?? 0;
                if (Policy.MaxExpressionLength.HasValue && totalLength > Policy.MaxExpressionLength.Value)
                {
                    execution.Diagnostics.Add(
                        "query-policy-expression-length-exceeded",
                        "The structured ordering keys exceed the configured UTF-16 length limit.",
                        0,
                        0,
                        Policy.MaxExpressionLength.Value,
                        totalLength,
                        inputPath: $"OrderBys[{index}].Property");
                    break;
                }
                keys.Add(info);
                if (!AdmitOrderingProperty(info.Property, $"OrderBys[{index}].Property", execution)) break;
                ++index;
            }
            if (execution.Diagnostics.ToReadOnlyList().Count > 0)
                return FailedOrderByResult(execution.Diagnostics.ToReadOnlyList());

            try
            {
                return OrderByParser.Parse(keys.ToArray(), CreateBuildArgument(string.Empty, null));
            }
            catch (QueryPolicyException exception)
            {
                return FailedOrderByResult(exception.Diagnostics);
            }
        }

        public EvaluationResult<IOrderedQueryable<T>> TryOrderBy<T>(IQueryable<T> source, string expression)
        {
            ValidateEntityType(typeof(T));
            if (source == null) throw new ArgumentNullException(nameof(source));
            return TrySort(source, TryBuildOrderByClause(expression));
        }

        public EvaluationResult<IOrderedQueryable<T>> TryOrderBy<T>(
            IQueryable<T> source,
            IEnumerable<OrderByInfo> orderByInfos)
        {
            ValidateEntityType(typeof(T));
            if (source == null) throw new ArgumentNullException(nameof(source));
            return TrySort(source, TryBuildOrderByClause(orderByInfos));
        }

        public EvaluationResult<IOrderedQueryable> TryOrderBy(IQueryable source, string expression)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            ValidateEntityType(source.ElementType);
            var clause = TryBuildOrderByClause(expression);
            if (!clause.Succeeded) return FailedSortResult<IOrderedQueryable>(clause);
            try
            {
                return new EvaluationResult<IOrderedQueryable>
                {
                    Succeeded = true,
                    Result = clause.Result.Sort(source)
                };
            }
            catch (Exception exception)
            {
                return new EvaluationResult<IOrderedQueryable> { Exception = exception };
            }
        }

        public EvaluationResult<IOrderedQueryable> TryOrderBy(
            IQueryable source,
            IEnumerable<OrderByInfo> orderByInfos)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            ValidateEntityType(source.ElementType);
            var clause = TryBuildOrderByClause(orderByInfos);
            if (!clause.Succeeded) return FailedSortResult<IOrderedQueryable>(clause);
            try
            {
                return new EvaluationResult<IOrderedQueryable>
                {
                    Succeeded = true,
                    Result = clause.Result.Sort(source)
                };
            }
            catch (Exception exception)
            {
                return new EvaluationResult<IOrderedQueryable> { Exception = exception };
            }
        }

        public EvaluationResult<IOrderedEnumerable<T>> TryOrderBy<T>(IEnumerable<T> source, string expression)
        {
            ValidateEntityType(typeof(T));
            if (source == null) throw new ArgumentNullException(nameof(source));
            return TrySort(source, TryBuildOrderByClause(expression));
        }

        public EvaluationResult<IOrderedEnumerable<T>> TryOrderBy<T>(
            IEnumerable<T> source,
            IEnumerable<OrderByInfo> orderByInfos)
        {
            ValidateEntityType(typeof(T));
            if (source == null) throw new ArgumentNullException(nameof(source));
            return TrySort(source, TryBuildOrderByClause(orderByInfos));
        }

        public IOrderedQueryable<T> OrderBy<T>(IQueryable<T> source, string expression)
            => ThrowIfSortFailed(TryOrderBy(source, expression));

        public IOrderedQueryable<T> OrderBy<T>(IQueryable<T> source, IEnumerable<OrderByInfo> orderByInfos)
            => ThrowIfSortFailed(TryOrderBy(source, orderByInfos));

        public IOrderedQueryable OrderBy(IQueryable source, string expression)
            => ThrowIfSortFailed(TryOrderBy(source, expression));

        public IOrderedQueryable OrderBy(IQueryable source, IEnumerable<OrderByInfo> orderByInfos)
            => ThrowIfSortFailed(TryOrderBy(source, orderByInfos));

        public IOrderedEnumerable<T> OrderBy<T>(IEnumerable<T> source, string expression)
            => ThrowIfSortFailed(TryOrderBy(source, expression));

        public IOrderedEnumerable<T> OrderBy<T>(IEnumerable<T> source, IEnumerable<OrderByInfo> orderByInfos)
            => ThrowIfSortFailed(TryOrderBy(source, orderByInfos));

        /// <summary>Builds a direct field predicate asynchronously and throws on failure.</summary>
        public async Task<Expression<Func<T, bool>>> BuildPredicateAsync<T>(
            string propertyName,
            ComparisonOperator comparisonOperator,
            object value,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            ValidateEntityType(typeof(T));
            return ThrowIfFailed(await TryBuildPredicateAsync<T>(
                propertyName,
                comparisonOperator,
                value,
                variableResolver,
                cancellationToken).ConfigureAwait(false));
        }

        /// <summary>Builds a direct field predicate asynchronously using the schema entity type.</summary>
        public async Task<LambdaExpression> BuildPredicateAsync(
            string propertyName,
            ComparisonOperator comparisonOperator,
            object value,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            return ThrowIfFailed(await TryBuildPredicateAsync(
                propertyName,
                comparisonOperator,
                value,
                variableResolver,
                cancellationToken).ConfigureAwait(false));
        }

        /// <summary>Attempts an asynchronous direct field predicate construction.</summary>
        public async Task<EvaluationResult<T, bool>> TryBuildPredicateAsync<T>(
            string propertyName,
            ComparisonOperator comparisonOperator,
            object value,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            ValidateEntityType(typeof(T));
            return (await TryBuildPredicateAsync(
                propertyName,
                comparisonOperator,
                value,
                variableResolver,
                cancellationToken).ConfigureAwait(false)).ToGeneric<T, bool>();
        }

        /// <summary>Attempts an asynchronous direct field predicate construction using the schema entity type.</summary>
        public async Task<EvaluationResult> TryBuildPredicateAsync(
            string propertyName,
            ComparisonOperator comparisonOperator,
            object value,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDirectArguments(propertyName, comparisonOperator);
            if (TryGetDirectPolicyDiagnostics(propertyName, comparisonOperator, value, out var diagnostics))
                return FailedPolicyResult(diagnostics);

            try
            {
                var clrPath = Schema.TryGetPolicyPathInfo(propertyName, out var mappedPath, out _, out _)
                    ? mappedPath
                    : propertyName;
                var argument = CreateBuildArgument(string.Empty, variableResolver);
                argument.CancellationToken = cancellationToken;
                if (Interpreter.IsMembership(comparisonOperator))
                    value = argument.SnapshotDirectMembershipCollection(value, GetPropertyType(clrPath));
                var predicate = await Interpreter.BuildPredicateAsync(
                    comparisonOperator,
                    clrPath,
                    value,
                    Schema.EntityType,
                    argument).ConfigureAwait(false);
                return new EvaluationResult
                {
                    Result = predicate,
                    Succeeded = true
                };
            }
            catch (QueryPolicyException exception)
            {
                return FailedPolicyResult(exception.Diagnostics);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new EvaluationResult { Exception = exception };
            }
        }

        internal bool IsPropertyQueryable(string sourcePath)
        {
            if (Schema.IsPublicSchema)
            {
                return Schema.TryResolveQueryField(sourcePath, out var field) &&
                    field.CanFilter &&
                    IsIncludedInAdditionalAllowlist(sourcePath);
            }
            return Schema.IsPropertyQueryable(sourcePath) &&
                IsIncludedInAdditionalAllowlist(sourcePath);
        }

        internal bool IsPropertySortable(string sourcePath)
        {
            if (Schema.IsPublicSchema)
            {
                return Schema.TryResolveQueryField(sourcePath, out var field) &&
                    field.CanSort &&
                    IsIncludedInAdditionalAllowlist(sourcePath);
            }
            return Schema.IsPropertyQueryable(sourcePath) &&
                IsIncludedInAdditionalAllowlist(sourcePath);
        }

        internal bool TryGetAllowedOperators(
            string sourcePath,
            out IReadOnlyCollection<ComparisonOperator> allowedOperators)
        {
            if (Schema.TryResolveQueryField(sourcePath, out var field) &&
                _allowedOperators.TryGetValue(field.Identity, out allowedOperators))
                return true;

            allowedOperators = null;
            return false;
        }

        internal bool IsOperatorAllowed(string sourcePath, ComparisonOperator comparisonOperator)
        {
            return !TryGetAllowedOperators(sourcePath, out var allowedOperators) ||
                allowedOperators.Contains(comparisonOperator);
        }

        internal bool AreOperatorsAllowed(IEnumerable<string> sourcePaths, ComparisonOperator comparisonOperator)
        {
            if (sourcePaths == null) throw new ArgumentNullException(nameof(sourcePaths));
            return sourcePaths.All(sourcePath => IsOperatorAllowed(sourcePath, comparisonOperator));
        }

        internal bool IsNavigationAllowed(string sourcePath, out int navigationDepth)
        {
            if (!Schema.TryGetPolicyPathInfo(sourcePath, out _, out navigationDepth, out _))
                return false;
            return !Policy.MaxNavigationDepth.HasValue ||
                navigationDepth <= Policy.MaxNavigationDepth.Value;
        }

        internal bool IsCollectionAccessAllowed(string sourcePath)
        {
            if (Schema.IsPublicSchema)
                return Schema.TryResolveQueryField(sourcePath, out _);
            if (!Schema.TryGetPolicyPathInfo(sourcePath, out _, out _, out var hasCollectionAccess))
                return false;
            return Policy.AllowCollectionAccess || !hasCollectionAccess;
        }

        internal void ValidateEntityType(Type entityType)
        {
            if (entityType == null) throw new ArgumentNullException(nameof(entityType));
            if (Schema.EntityType != entityType)
                throw new ArgumentException("The schema entity type must match the requested entity type.", nameof(entityType));
        }

        private sealed class StructuredPredicatePreparation
        {
            internal readonly List<(Filter Filter, string Path)> FlatFilters = new List<(Filter Filter, string Path)>();
            internal readonly List<List<(Filter Filter, string Path)>> Groups = new List<List<(Filter Filter, string Path)>>();
            internal readonly List<string> InvalidProperties = new List<string>();
            internal readonly List<string> InvalidOperators = new List<string>();
            internal EvaluationResult Failure;
            internal bool IsGroupBased;
        }

        private sealed class ConditionPreparation
        {
            internal ExpressionSemanticAnalysisResult WhereAnalysis;
            internal StructuredPredicatePreparation Filters;
            internal StructuredPredicatePreparation Groups;
            internal EvaluationResult<OrderByClause> OrderBy;
            internal EvaluationResult Failure;
        }

        private Condition BuildConditionCore(ConditionOptions options, VariableResolver variableResolver)
        {
            var preparation = PrepareCondition(options);
            if (preparation.Failure != null) return InvalidCondition(preparation.Failure);

            var predicates = new List<LambdaExpression>();
            if (!string.IsNullOrEmpty(options.Where))
            {
                var where = ParsePredicate(options.Where, variableResolver);
                if (!where.Succeeded) return InvalidCondition(where);
                predicates.Add(where.Result);
            }
            if (preparation.Filters != null && preparation.Filters.FlatFilters.Count > 0)
            {
                var filters = TryBuildStructuredPredicate(
                    preparation.Filters.FlatFilters.Select(item => item.Filter),
                    null,
                    variableResolver,
                    "Filters");
                if (!filters.Succeeded) return InvalidCondition(filters);
                predicates.Add(filters.Result);
            }
            if (preparation.Groups != null && preparation.Groups.Groups.Count > 0)
            {
                var groups = TryBuildStructuredPredicate(
                    null,
                    ToFilterGroups(preparation.Groups),
                    variableResolver,
                    "FilterGroups");
                if (!groups.Succeeded) return InvalidCondition(groups);
                predicates.Add(groups.Result);
            }

            if (options.FilterTree != null)
            {
                var tree = TryBuildPredicate(options.FilterTree, variableResolver);
                if (!tree.Succeeded)
                    return InvalidCondition(ProjectFilterTreeEvaluation(tree));
                predicates.Add(tree.Result);
            }

            return new Condition
            {
                IsValid = true,
                Predicates = predicates,
                OrderByClause = preparation.OrderBy?.Result
            };
        }

        private async Task<Condition> BuildConditionCoreAsync(
            ConditionOptions options,
            VariableResolver variableResolver,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var preparation = PrepareCondition(options, cancellationToken);
            if (preparation.Failure != null) return InvalidCondition(preparation.Failure);

            var predicates = new List<LambdaExpression>();
            if (!string.IsNullOrEmpty(options.Where))
            {
                var where = await ParsePredicateAsync(options.Where, variableResolver, cancellationToken)
                    .ConfigureAwait(false);
                if (!where.Succeeded) return InvalidCondition(where);
                predicates.Add(where.Result);
            }
            if (preparation.Filters != null && preparation.Filters.FlatFilters.Count > 0)
            {
                var filters = await TryBuildStructuredPredicateAsync(
                    preparation.Filters.FlatFilters.Select(item => item.Filter),
                    null,
                    variableResolver,
                    "Filters",
                    cancellationToken).ConfigureAwait(false);
                if (!filters.Succeeded) return InvalidCondition(filters);
                predicates.Add(filters.Result);
            }
            if (preparation.Groups != null && preparation.Groups.Groups.Count > 0)
            {
                var groups = await TryBuildStructuredPredicateAsync(
                    null,
                    ToFilterGroups(preparation.Groups),
                    variableResolver,
                    "FilterGroups",
                    cancellationToken).ConfigureAwait(false);
                if (!groups.Succeeded) return InvalidCondition(groups);
                predicates.Add(groups.Result);
            }

            if (options.FilterTree != null)
            {
                var tree = await TryBuildPredicateAsync(
                    options.FilterTree,
                    variableResolver,
                    cancellationToken).ConfigureAwait(false);
                if (!tree.Succeeded)
                    return InvalidCondition(ProjectFilterTreeEvaluation(tree));
                predicates.Add(tree.Result);
            }

            return new Condition
            {
                IsValid = true,
                Predicates = predicates,
                OrderByClause = preparation.OrderBy?.Result
            };
        }

        private ConditionPreparation PrepareCondition(
            ConditionOptions options,
            CancellationToken cancellationToken = default)
        {
            var preparation = new ConditionPreparation();
            if (!string.IsNullOrEmpty(options.Where))
            {
                preparation.WhereAnalysis = AnalyzeExpression(options.Where);
                var policyFailures = preparation.WhereAnalysis.Diagnostics
                    .Where(diagnostic =>
                        diagnostic.Code.StartsWith("query-policy-", StringComparison.Ordinal) ||
                        diagnostic.Code == "property-not-queryable")
                    .ToArray();
                if (policyFailures.Length > 0)
                {
                    preparation.Failure = FailedPolicyResult(policyFailures);
                    return preparation;
                }
            }

            if (options.Filters != null)
            {
                preparation.Filters = PrepareStructuredPredicate(options.Filters, null, "Filters", cancellationToken);
                if (preparation.Filters.Failure != null)
                {
                    preparation.Failure = preparation.Filters.Failure;
                    return preparation;
                }
            }
            if (options.FilterGroups != null)
            {
                preparation.Groups = PrepareStructuredPredicate(null, options.FilterGroups, "FilterGroups", cancellationToken);
                if (preparation.Groups.Failure != null)
                {
                    preparation.Failure = preparation.Groups.Failure;
                    return preparation;
                }
            }

            if (options.FilterTree != null)
            {
                var validation = ValidateFilterTree(
                    options.FilterTree,
                    variables: null,
                    allowUnresolvedVariables: true,
                    new QueryPolicyExecution(Policy));
                if (!validation.Succeeded)
                {
                    preparation.Failure = FailureFromFilterTreeValidation(validation, "/FilterTree");
                    return preparation;
                }
            }

            if (!string.IsNullOrWhiteSpace(options.OrderBy))
                preparation.OrderBy = TryBuildOrderByClause(options.OrderBy);
            else if (options.OrderBys != null && options.OrderBys.Count > 0)
                preparation.OrderBy = TryBuildOrderByClause(options.OrderBys);

            if (preparation.OrderBy != null && !preparation.OrderBy.Succeeded)
            {
                preparation.Failure = new EvaluationResult
                {
                    Diagnostics = preparation.OrderBy.Diagnostics,
                    Exception = preparation.OrderBy.Exception,
                    InvalidProperties = preparation.OrderBy.InvalidProperties,
                    InvalidOrderByDirections = preparation.OrderBy.InvalidOrderByDirections
                };
                return preparation;
            }

            var combinedFailure = GetConditionAggregateFailure(options, preparation);
            if (combinedFailure != null)
            {
                preparation.Failure = combinedFailure;
                return preparation;
            }
            if (preparation.WhereAnalysis != null &&
                (!preparation.WhereAnalysis.IsComplete ||
                 preparation.WhereAnalysis.SyntaxDiagnostics.Count > 0))
            {
                preparation.Failure = new EvaluationResult
                {
                    Diagnostics = preparation.WhereAnalysis.Diagnostics,
                    Exception = new InvalidOperationException("The Where expression is not syntactically complete.")
                };
            }
            return preparation;
        }

        private EvaluationResult GetConditionAggregateFailure(
            ConditionOptions options,
            ConditionPreparation preparation)
        {
            var execution = new QueryPolicyExecution(Policy);
            long length = options.Where?.Length ?? 0;
            long conditions = preparation.WhereAnalysis?.AtomicConditionCount ?? 0;
            if (Policy.MaxExpressionLength.HasValue && length > Policy.MaxExpressionLength.Value)
            {
                execution.Diagnostics.Add(
                    "query-policy-expression-length-exceeded",
                    "The condition inputs exceed the configured UTF-16 length limit.",
                    0,
                    0,
                    Policy.MaxExpressionLength.Value,
                    length,
                    inputPath: "Where");
                return FailedPolicyResult(execution.Diagnostics.ToReadOnlyList());
            }
            if (Policy.MaxAtomicConditions.HasValue && conditions > Policy.MaxAtomicConditions.Value)
            {
                execution.Diagnostics.Add(
                    "query-policy-condition-count-exceeded",
                    "The condition inputs contain more atomic conditions than the configured limit.",
                    0,
                    0,
                    Policy.MaxAtomicConditions.Value,
                    conditions,
                    inputPath: "Where");
                return FailedPolicyResult(execution.Diagnostics.ToReadOnlyList());
            }

            foreach (var item in EnumerateConditionFilters(preparation))
            {
                var filter = item.Filter;
                length += (long)(filter.Property?.Length ?? 0) +
                    (filter.Operator?.Length ?? 0) +
                    (filter.Value?.Length ?? 0);
                if (Policy.MaxExpressionLength.HasValue && length > Policy.MaxExpressionLength.Value)
                {
                    execution.Diagnostics.Add(
                        "query-policy-expression-length-exceeded",
                        "The condition inputs exceed the configured UTF-16 length limit.",
                        0,
                        0,
                        Policy.MaxExpressionLength.Value,
                        length,
                        inputPath: item.Path + ".Value");
                    return FailedPolicyResult(execution.Diagnostics.ToReadOnlyList());
                }

                conditions++;
                if (Policy.MaxAtomicConditions.HasValue && conditions > Policy.MaxAtomicConditions.Value)
                {
                    execution.Diagnostics.Add(
                        "query-policy-condition-count-exceeded",
                        "The condition inputs contain more atomic conditions than the configured limit.",
                        0,
                        0,
                        Policy.MaxAtomicConditions.Value,
                        conditions,
                        inputPath: item.Path + ".Operator");
                    return FailedPolicyResult(execution.Diagnostics.ToReadOnlyList());
                }
            }

            if (options.FilterTree != null)
            {
                foreach (var item in EnumerateTreeConditions(options.FilterTree))
                {
                    length += (long)item.Node.Field.Length +
                        item.Node.Operator.Length +
                        GetFilterTreeValueTextLength(item.Node.Value);
                    if (Policy.MaxExpressionLength.HasValue && length > Policy.MaxExpressionLength.Value)
                    {
                        execution.Diagnostics.Add(
                            "query-policy-expression-length-exceeded",
                            "The condition inputs exceed the configured UTF-16 length limit.",
                            0,
                            0,
                            Policy.MaxExpressionLength.Value,
                            length,
                            inputPath: item.Path + "/value");
                        return FailedPolicyResult(execution.Diagnostics.ToReadOnlyList());
                    }

                    conditions++;
                    if (Policy.MaxAtomicConditions.HasValue && conditions > Policy.MaxAtomicConditions.Value)
                    {
                        execution.Diagnostics.Add(
                            "query-policy-condition-count-exceeded",
                            "The condition inputs contain more atomic conditions than the configured limit.",
                            0,
                            0,
                            Policy.MaxAtomicConditions.Value,
                            conditions,
                            inputPath: item.Path + "/op");
                        return FailedPolicyResult(execution.Diagnostics.ToReadOnlyList());
                    }
                }
            }
            return null;
        }

        private static IEnumerable<(FilterNode Node, string Path)> EnumerateTreeConditions(FilterNode tree)
        {
            var pending = new Stack<(FilterNode Node, string Path)>();
            pending.Push((tree, "/FilterTree"));
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (current.Node.Kind == FilterNodeKind.Condition)
                {
                    yield return current;
                    continue;
                }

                var member = current.Node.Kind == FilterNodeKind.And
                    ? "and"
                    : current.Node.Kind == FilterNodeKind.Or ? "or" : "not";
                var nodePath = FilterTreeDiagnosticProjection.AppendPointer(current.Path, member);
                if (current.Node.Kind == FilterNodeKind.Not)
                {
                    pending.Push((current.Node.Child, nodePath));
                    continue;
                }
                for (var index = current.Node.Children.Count - 1; index >= 0; --index)
                    pending.Push((
                        current.Node.Children[index],
                        FilterTreeDiagnosticProjection.AppendPointer(nodePath, index.ToString())));
            }
        }

        private static long GetFilterTreeValueTextLength(FilterValue value)
        {
            switch (value.Kind)
            {
                case FilterValueKind.Null:
                    return 4;
                case FilterValueKind.Boolean:
                    return value.BooleanValue ? 4 : 5;
                case FilterValueKind.Number:
                case FilterValueKind.String:
                case FilterValueKind.Variable:
                    return value.Text.Length;
                case FilterValueKind.Collection:
                    long length = 0;
                    foreach (var item in value.Items)
                    {
                        var itemLength = GetFilterTreeValueTextLength(item);
                        length = long.MaxValue - length < itemLength ? long.MaxValue : length + itemLength;
                    }
                    return length;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), "Unknown filter value kind.");
            }
        }

        private static IEnumerable<(Filter Filter, string Path)> EnumerateConditionFilters(
            ConditionPreparation preparation)
        {
            if (preparation.Filters != null)
                foreach (var filter in preparation.Filters.FlatFilters) yield return filter;
            if (preparation.Groups != null)
                foreach (var group in preparation.Groups.Groups)
                    foreach (var filter in group)
                        yield return filter;
        }

        private static IEnumerable<FilterGroup> ToFilterGroups(StructuredPredicatePreparation preparation)
        {
            return preparation.Groups.Select(group => new FilterGroup
            {
                Filters = group.Select(item => item.Filter).ToList()
            }).ToArray();
        }

        private static Condition InvalidCondition(EvaluationResult failure)
        {
            var exceptions = failure.Exception == null
                ? new List<Exception>()
                : new List<Exception> { failure.Exception };
            return new Condition
            {
                IsValid = false,
                Error = new ConditionBase.ErrorInfo
                {
                    Exceptions = exceptions,
                    EvaluationResult = failure
                }
            };
        }

        private EvaluationResult TryBuildStructuredPredicate(
            IEnumerable<Filter> filters,
            IEnumerable<FilterGroup> filterGroups,
            VariableResolver variableResolver,
            string inputPrefix)
        {
            var preparation = PrepareStructuredPredicate(filters, filterGroups, inputPrefix);
            if (preparation.Failure != null) return preparation.Failure;

            try
            {
                var argument = CreateBuildArgument(string.Empty, variableResolver);
                var parameter = Schema.EntityType.CreateParameterExpression();
                Expression body;
                if (preparation.IsGroupBased)
                {
                    body = null;
                    foreach (var group in preparation.Groups)
                    {
                        var groupBody = BuildStructuredGroup(parameter, group, argument);
                        body = body == null ? groupBody : Expression.OrElse(body, groupBody);
                    }
                }
                else
                {
                    body = BuildStructuredGroup(parameter, preparation.FlatFilters, argument);
                }

                if (body == null) body = Expression.Constant(true);
                return new EvaluationResult
                {
                    Result = Expression.Lambda(body, parameter),
                    Succeeded = true
                };
            }
            catch (QueryPolicyException exception)
            {
                return FailedPolicyResult(exception.Diagnostics);
            }
            catch (Exception exception)
            {
                return new EvaluationResult { Exception = exception };
            }
        }

        private async Task<EvaluationResult> TryBuildStructuredPredicateAsync(
            IEnumerable<Filter> filters,
            IEnumerable<FilterGroup> filterGroups,
            VariableResolver variableResolver,
            string inputPrefix,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var preparation = PrepareStructuredPredicate(filters, filterGroups, inputPrefix, cancellationToken);
            if (preparation.Failure != null) return preparation.Failure;

            try
            {
                var argument = CreateBuildArgument(string.Empty, variableResolver);
                argument.CancellationToken = cancellationToken;
                var parameter = Schema.EntityType.CreateParameterExpression();
                Expression body;
                if (preparation.IsGroupBased)
                {
                    body = null;
                    foreach (var group in preparation.Groups)
                    {
                        var groupBody = await BuildStructuredGroupAsync(
                            parameter,
                            group,
                            argument,
                            cancellationToken).ConfigureAwait(false);
                        body = body == null ? groupBody : Expression.OrElse(body, groupBody);
                    }
                }
                else
                {
                    body = await BuildStructuredGroupAsync(
                        parameter,
                        preparation.FlatFilters,
                        argument,
                        cancellationToken).ConfigureAwait(false);
                }

                if (body == null) body = Expression.Constant(true);
                return new EvaluationResult
                {
                    Result = Expression.Lambda(body, parameter),
                    Succeeded = true
                };
            }
            catch (QueryPolicyException exception)
            {
                return FailedPolicyResult(exception.Diagnostics);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new EvaluationResult { Exception = exception };
            }
        }

        private StructuredPredicatePreparation PrepareStructuredPredicate(
            IEnumerable<Filter> filters,
            IEnumerable<FilterGroup> filterGroups,
            string inputPrefix,
            CancellationToken cancellationToken = default)
        {
            var preparation = new StructuredPredicatePreparation
            {
                IsGroupBased = filterGroups != null
            };
            var execution = new QueryPolicyExecution(Policy);
            long totalLength = 0;
            var flatIndex = 0;

            if (filters != null)
            {
                foreach (var filter in filters)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (filter == null)
                    {
                        preparation.Failure = new EvaluationResult { Exception = new InvalidOperationException("Filter cannot be null.") };
                        return preparation;
                    }
                    var path = inputPrefix == "Filter" ? inputPrefix : $"{inputPrefix}[{flatIndex}]";
                    var item = (filter, path);
                    preparation.FlatFilters.Add(item);
                    if (!AdmitStructuredFilter(item, execution, ref totalLength, preparation.InvalidProperties, preparation.InvalidOperators))
                        break;
                    ++flatIndex;
                }
            }

            if (filterGroups != null && execution.Diagnostics.ToReadOnlyList().Count == 0)
            {
                var groupIndex = 0;
                foreach (var filterGroup in filterGroups)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (filterGroup == null)
                    {
                        preparation.Failure = new EvaluationResult { Exception = new InvalidOperationException("FilterGroup cannot be null.") };
                        return preparation;
                    }
                    var groupPath = inputPrefix == "FilterGroup" ? inputPrefix : $"{inputPrefix}[{groupIndex}]";
                    if (Policy.MaxParenthesisDepth.HasValue && Policy.MaxParenthesisDepth.Value < 1)
                    {
                        execution.Diagnostics.Add(
                            "query-policy-parenthesis-depth-exceeded",
                            "The structured filter group exceeds the configured group depth.",
                            0,
                            0,
                            Policy.MaxParenthesisDepth.Value,
                            1,
                            inputPath: groupPath);
                        break;
                    }
                    if (filterGroup.Filters == null)
                    {
                        preparation.Failure = new EvaluationResult { Exception = new InvalidOperationException("Filters of FilterGroup cannot be null.") };
                        return preparation;
                    }

                    var group = new List<(Filter Filter, string Path)>();
                    preparation.Groups.Add(group);
                    for (var filterIndex = 0; filterIndex < filterGroup.Filters.Count; ++filterIndex)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var filter = filterGroup.Filters[filterIndex];
                        if (filter == null)
                        {
                            preparation.Failure = new EvaluationResult { Exception = new InvalidOperationException("Filter cannot be null.") };
                            return preparation;
                        }
                        var item = (filter, $"{groupPath}.Filters[{filterIndex}]");
                        group.Add(item);
                        if (!AdmitStructuredFilter(item, execution, ref totalLength, preparation.InvalidProperties, preparation.InvalidOperators))
                            break;
                    }
                    if (execution.Diagnostics.ToReadOnlyList().Count > 0) break;
                    ++groupIndex;
                }
            }

            var policyDiagnostics = execution.Diagnostics.ToReadOnlyList();
            if (policyDiagnostics.Count > 0)
                preparation.Failure = FailedPolicyResult(policyDiagnostics);
            else if (preparation.InvalidProperties.Count > 0 || preparation.InvalidOperators.Count > 0)
            {
                preparation.Failure = new EvaluationResult
                {
                    InvalidProperties = preparation.InvalidProperties,
                    InvalidOperators = preparation.InvalidOperators
                };
            }
            return preparation;
        }

        private bool AdmitStructuredFilter(
            (Filter Filter, string Path) item,
            QueryPolicyExecution execution,
            ref long totalLength,
            ICollection<string> invalidProperties,
            ICollection<string> invalidOperators)
        {
            var filter = item.Filter;
            var operatorText = filter.Operator ?? string.Empty;
            var valueText = filter.Value;
            totalLength += (long)(filter.Property?.Length ?? 0) + operatorText.Length + (valueText?.Length ?? 0);
            if (Policy.MaxExpressionLength.HasValue && totalLength > Policy.MaxExpressionLength.Value)
            {
                execution.Diagnostics.Add(
                    "query-policy-expression-length-exceeded",
                    "The structured filters exceed the configured UTF-16 length limit.",
                    0,
                    0,
                    Policy.MaxExpressionLength.Value,
                    totalLength,
                    inputPath: item.Path + ".Value");
                return false;
            }

            if (!execution.TryCountCondition(0, 0, item.Path + ".Operator")) return false;
            var normalizedOperator = Interpreter.NormalizeComparisonOperator(operatorText);
            var isKnownOperator = Interpreter.ComparisonOperators.Contains(
                normalizedOperator,
                StringComparer.OrdinalIgnoreCase);
            if (!isKnownOperator)
                invalidOperators.Add(operatorText);

            if (isKnownOperator &&
                Interpreter.IsMembership(normalizedOperator) &&
                valueText != null &&
                !QueryPolicySourceScanner.TryCountMembershipFragment(
                    valueText,
                    execution,
                    item.Path + ".Value"))
                return false;

            var property = filter.Property;
            if (string.IsNullOrWhiteSpace(property))
            {
                invalidProperties.Add(property);
                return true;
            }
            if (!Schema.TryGetPolicyPathInfo(property, out _, out var navigationDepth, out var collectionAccess) ||
                !IsPropertyQueryable(property))
            {
                execution.Diagnostics.Add(
                    "property-not-queryable",
                    "The field is not permitted for querying.",
                    0,
                    0,
                    inputPath: item.Path + ".Property");
                return false;
            }

            if (Policy.MaxNavigationDepth.HasValue && navigationDepth > Policy.MaxNavigationDepth.Value)
            {
                execution.Diagnostics.Add(
                    "query-policy-navigation-depth-exceeded",
                    "The field exceeds the configured entity navigation depth.",
                    0,
                    0,
                    Policy.MaxNavigationDepth.Value,
                    navigationDepth,
                    inputPath: item.Path + ".Property");
                return false;
            }
            if (!Policy.AllowCollectionAccess && collectionAccess)
            {
                execution.Diagnostics.Add(
                    "query-policy-collection-access-denied",
                    "Traversal through entity collection fields is not permitted.",
                    0,
                    0,
                    inputPath: item.Path + ".Property");
                return false;
            }
            if (isKnownOperator &&
                !IsOperatorAllowed(property, normalizedOperator.GetComparisonOperator()))
            {
                execution.Diagnostics.Add(
                    "query-policy-operator-denied",
                    "The comparison operator is not permitted for this field.",
                    0,
                    0,
                    inputPath: item.Path + ".Operator");
                return false;
            }
            return true;
        }

        private static Expression BuildStructuredGroup(
            ParameterExpression parameter,
            IEnumerable<(Filter Filter, string Path)> filters,
            BuildArgument argument)
        {
            Expression body = null;
            foreach (var item in filters)
            {
                argument.MembershipInputPath = item.Path + ".Value";
                var next = FilterExtensions.BuildCondition(parameter, item.Filter, argument);
                body = body == null ? next : Expression.AndAlso(body, next);
            }
            return body ?? Expression.Constant(true);
        }

        private static async Task<Expression> BuildStructuredGroupAsync(
            ParameterExpression parameter,
            IEnumerable<(Filter Filter, string Path)> filters,
            BuildArgument argument,
            CancellationToken cancellationToken)
        {
            Expression body = null;
            foreach (var item in filters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                argument.MembershipInputPath = item.Path + ".Value";
                var next = await FilterExtensions.BuildConditionAsync(
                    parameter,
                    item.Filter,
                    argument,
                    cancellationToken).ConfigureAwait(false);
                body = body == null ? next : Expression.AndAlso(body, next);
            }
            return body ?? Expression.Constant(true);
        }

        private bool AdmitOrderingProperty(
            string sourcePath,
            string inputPath,
            QueryPolicyExecution execution)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !Schema.TryGetPolicyPathInfo(sourcePath, out _, out var navigationDepth, out var collectionAccess) ||
                !IsPropertySortable(sourcePath))
            {
                execution.Diagnostics.Add(
                    "property-not-queryable",
                    "The ordering field is not permitted.",
                    0,
                    0,
                    inputPath: inputPath);
                return false;
            }
            if (Policy.MaxNavigationDepth.HasValue && navigationDepth > Policy.MaxNavigationDepth.Value)
            {
                execution.Diagnostics.Add(
                    "query-policy-navigation-depth-exceeded",
                    "The ordering field exceeds the configured entity navigation depth.",
                    0,
                    0,
                    Policy.MaxNavigationDepth.Value,
                    navigationDepth,
                    inputPath: inputPath);
                return false;
            }
            if (!Policy.AllowCollectionAccess && collectionAccess)
            {
                execution.Diagnostics.Add(
                    "query-policy-collection-access-denied",
                    "Ordering through entity collection fields is not permitted.",
                    0,
                    0,
                    inputPath: inputPath);
                return false;
            }
            return true;
        }

        private static EvaluationResult<OrderByClause> FailedOrderByResult(
            IReadOnlyList<ExpressionDiagnostic> diagnostics)
        {
            return new EvaluationResult<OrderByClause>
            {
                Succeeded = false,
                Result = null,
                Diagnostics = diagnostics,
                Exception = new QueryPolicyException(diagnostics)
            };
        }

        private static EvaluationResult<IOrderedQueryable<T>> TrySort<T>(
            IQueryable<T> source,
            EvaluationResult<OrderByClause> clause)
        {
            if (!clause.Succeeded) return FailedSortResult<IOrderedQueryable<T>>(clause);
            try
            {
                return new EvaluationResult<IOrderedQueryable<T>>
                {
                    Succeeded = true,
                    Result = clause.Result.Sort(source)
                };
            }
            catch (Exception exception)
            {
                return new EvaluationResult<IOrderedQueryable<T>> { Exception = exception };
            }
        }

        private static EvaluationResult<IOrderedEnumerable<T>> TrySort<T>(
            IEnumerable<T> source,
            EvaluationResult<OrderByClause> clause)
        {
            if (!clause.Succeeded) return FailedSortResult<IOrderedEnumerable<T>>(clause);
            try
            {
                return new EvaluationResult<IOrderedEnumerable<T>>
                {
                    Succeeded = true,
                    Result = clause.Result.Sort(source)
                };
            }
            catch (Exception exception)
            {
                return new EvaluationResult<IOrderedEnumerable<T>> { Exception = exception };
            }
        }

        private static EvaluationResult<TResult> FailedSortResult<TResult>(EvaluationResult<OrderByClause> clause)
        {
            return new EvaluationResult<TResult>
            {
                Succeeded = false,
                Result = default(TResult),
                Exception = clause.Exception,
                Diagnostics = clause.Diagnostics,
                InvalidProperties = clause.InvalidProperties,
                InvalidOrderByDirections = clause.InvalidOrderByDirections
            };
        }

        private static TResult ThrowIfSortFailed<TResult>(EvaluationResult<TResult> result)
        {
            if (result.Succeeded) return result.Result;
            if (result.Exception != null) ExceptionDispatchInfo.Capture(result.Exception).Throw();
            throw new InvalidOperationException("Ordering failed.");
        }

        private FilterOperationResult<FilterNode> ValidateFilterTree(
            FilterNode tree,
            ExpressionVariableSchema variables,
            bool allowUnresolvedVariables,
            QueryPolicyExecution execution)
        {
            if (!FilterTreePolicyScanner.TryScan(tree, this, execution))
                return FilterOperationResult<FilterNode>.Failure(
                    execution.Diagnostics.ToReadOnlyList(),
                    execution.Diagnostics.IsTruncated);

            return FilterTreeSemanticValidator.Validate(
                tree,
                Schema,
                variables,
                allowUnresolvedVariables);
        }

        private EvaluationResult FailureFromFilterTreeValidation(
            FilterOperationResult<FilterNode> validation,
            string inputPathPrefix = null)
        {
            var diagnostics = inputPathPrefix == null
                ? validation.Diagnostics
                : FilterTreeDiagnosticProjection.Project(validation.Diagnostics, inputPathPrefix);
            if (diagnostics.Any(diagnostic =>
                    diagnostic.Code.StartsWith("query-policy-", StringComparison.Ordinal) ||
                    diagnostic.Code == "property-not-queryable"))
                return FailedPolicyResult(diagnostics);
            return new EvaluationResult { Diagnostics = diagnostics };
        }

        private static EvaluationResult ProjectFilterTreeEvaluation(EvaluationResult result)
        {
            return new EvaluationResult
            {
                Exception = result.Exception,
                Diagnostics = FilterTreeDiagnosticProjection.Project(result.Diagnostics, "/FilterTree"),
                InvalidProperties = result.InvalidProperties,
                InvalidOperators = result.InvalidOperators,
                InvalidVariables = result.InvalidVariables,
                InvalidValues = result.InvalidValues
            };
        }

        private bool TryGetRuntimePolicyDiagnostics(
            string expression,
            out IReadOnlyList<ExpressionDiagnostic> diagnostics)
        {
            var analysis = AnalyzeExpression(expression);
            diagnostics = Array.AsReadOnly(analysis.Diagnostics
                .Where(diagnostic =>
                    diagnostic.Code.StartsWith("query-policy-", StringComparison.Ordinal) ||
                    diagnostic.Code == "property-not-queryable")
                .ToArray());
            return diagnostics.Count > 0;
        }

        private BuildArgument CreateBuildArgument(
            string expression,
            VariableResolver variableResolver,
            QueryPolicyExecution policyExecution = null)
        {
            return new BuildArgument
            {
                EvaluationType = Schema.EntityType,
                Schema = Schema,
                ValidProperties = Schema.ValidProperties,
                PropertyMapping = Schema.PropertyMapping.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase),
                VariableResolver = variableResolver ?? new VariableResolver(),
                QueryContext = this,
                PolicyExecution = policyExecution ?? new QueryPolicyExecution(Policy),
                SourceOffset = LeadingWhitespaceLength(expression),
                SourceExpression = expression,
                MembershipInputPath = string.IsNullOrEmpty(expression) ? "Value" : null
            };
        }

        private static int LeadingWhitespaceLength(string expression)
        {
            var index = 0;
            while (index < expression.Length && char.IsWhiteSpace(expression[index])) ++index;
            return index;
        }

        private static void ValidateExpressionArgument(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException("An expression is required.", nameof(expression));
        }

        private static void ValidateDirectArguments(string propertyName, ComparisonOperator comparisonOperator)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                throw new ArgumentException("A property name is required.", nameof(propertyName));
            if (!Enum.IsDefined(typeof(ComparisonOperator), comparisonOperator))
                throw new ArgumentOutOfRangeException(nameof(comparisonOperator));
        }

        private bool TryGetDirectPolicyDiagnostics(
            string propertyName,
            ComparisonOperator comparisonOperator,
            object value,
            out IReadOnlyList<ExpressionDiagnostic> diagnostics)
        {
            var execution = new QueryPolicyExecution(Policy);
            var operatorText = CanonicalOperatorText(comparisonOperator);
            var valueText = value as string;
            var observedLength = (long)propertyName.Length + operatorText.Length + (valueText?.Length ?? 0);
            if (Policy.MaxExpressionLength.HasValue && observedLength > Policy.MaxExpressionLength.Value)
            {
                var limit = Policy.MaxExpressionLength.Value;
                var inputPath = limit < propertyName.Length
                    ? "Property"
                    : limit < propertyName.Length + operatorText.Length
                        ? "Operator"
                        : "Value";
                execution.Diagnostics.Add(
                    "query-policy-expression-length-exceeded",
                    "The direct predicate input exceeds the configured UTF-16 length limit.",
                    0,
                    0,
                    limit,
                    observedLength,
                    inputPath: inputPath);
                diagnostics = execution.Diagnostics.ToReadOnlyList();
                return true;
            }

            if (!execution.TryCountCondition(0, 0, "Operator"))
            {
                diagnostics = execution.Diagnostics.ToReadOnlyList();
                return true;
            }

            if (Interpreter.IsMembership(comparisonOperator) &&
                value is string membershipText &&
                !QueryPolicySourceScanner.TryCountMembershipFragment(membershipText, execution, "Value"))
            {
                diagnostics = execution.Diagnostics.ToReadOnlyList();
                return true;
            }

            if (!Schema.TryGetPolicyPathInfo(propertyName, out _, out var navigationDepth, out var collectionAccess) ||
                !IsPropertyQueryable(propertyName))
            {
                execution.Diagnostics.Add(
                    "property-not-queryable",
                    "The field is not permitted for querying.",
                    0,
                    0,
                    inputPath: "Property");
            }
            else
            {
                if (Policy.MaxNavigationDepth.HasValue &&
                    navigationDepth > Policy.MaxNavigationDepth.Value)
                {
                    execution.Diagnostics.Add(
                        "query-policy-navigation-depth-exceeded",
                        "The field exceeds the configured entity navigation depth.",
                        0,
                        0,
                        Policy.MaxNavigationDepth.Value,
                        navigationDepth,
                        inputPath: "Property");
                }
                if (!Policy.AllowCollectionAccess && collectionAccess)
                {
                    execution.Diagnostics.Add(
                        "query-policy-collection-access-denied",
                        "Traversal through entity collection fields is not permitted.",
                        0,
                        0,
                        inputPath: "Property");
                }
                if (!IsOperatorAllowed(propertyName, comparisonOperator))
                {
                    execution.Diagnostics.Add(
                        "query-policy-operator-denied",
                        "The comparison operator is not permitted for this field.",
                        0,
                        0,
                        inputPath: "Operator");
                }
            }

            diagnostics = execution.Diagnostics.ToReadOnlyList();
            return diagnostics.Count > 0;
        }

        private Type GetPropertyType(string clrPath)
        {
            if (Schema.TryResolveQueryField(clrPath, out var field))
                return field.ClrType;
            var current = Schema.EntityType;
            foreach (var segment in ExpressionSchema.NormalizePath(clrPath).Split('.'))
            {
                var property = current.GetProperty(segment);
                if (property == null) return typeof(object);
                current = property.PropertyType;
            }
            return current;
        }

        private static string CanonicalOperatorText(ComparisonOperator comparisonOperator)
        {
            switch (comparisonOperator)
            {
                case ComparisonOperator.Equal: return "=";
                case ComparisonOperator.NotEqual: return "!=";
                case ComparisonOperator.LessThan: return "<";
                case ComparisonOperator.LessThanOrEqual: return "<=";
                case ComparisonOperator.GreaterThan: return ">";
                case ComparisonOperator.GreaterThanOrEqual: return ">=";
                case ComparisonOperator.Contains: return "contains";
                case ComparisonOperator.StartsWith: return "startswith";
                case ComparisonOperator.EndsWith: return "endswith";
                case ComparisonOperator.In: return "in";
                case ComparisonOperator.NotIn: return "not in";
                default: throw new ArgumentOutOfRangeException(nameof(comparisonOperator));
            }
        }

        private static LambdaExpression ThrowIfFailed(EvaluationResult result)
        {
            if (result.Succeeded) return result.Result;
            if (result.Exception != null) ExceptionDispatchInfo.Capture(result.Exception).Throw();
            throw new InvalidOperationException("Predicate construction failed.");
        }

        private static Expression<Func<T, bool>> ThrowIfFailed<T>(EvaluationResult<T, bool> result)
        {
            if (result.Succeeded) return result.Result;
            if (result.Exception != null) ExceptionDispatchInfo.Capture(result.Exception).Throw();
            throw new InvalidOperationException("Predicate construction failed.");
        }

        private static EvaluationResult FailedPolicyResult(IReadOnlyList<ExpressionDiagnostic> diagnostics)
        {
            return new EvaluationResult
            {
                Succeeded = false,
                Result = null,
                Diagnostics = diagnostics,
                Exception = new QueryPolicyException(diagnostics)
            };
        }

        private static IReadOnlyList<string> SnapshotValidProperties(IEnumerable<string> validProperties)
        {
            if (validProperties == null) return Array.AsReadOnly(Array.Empty<string>());

            var snapshot = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in validProperties)
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Additional allowed property names cannot be null or whitespace.", nameof(validProperties));

                var path = ExpressionSchema.NormalizePath(value);
                if (!seen.Add(path))
                    throw new ArgumentException($"Duplicate additional allowed property name '{value}'.", nameof(validProperties));
                snapshot.Add(path);
            }

            return Array.AsReadOnly(snapshot.ToArray());
        }

        private static IReadOnlyDictionary<string, IReadOnlyCollection<ComparisonOperator>> BindAllowedOperators(
            ExpressionSchema schema,
            QueryPolicy policy)
        {
            var allowedByPath = new Dictionary<string, IReadOnlyCollection<ComparisonOperator>>(StringComparer.OrdinalIgnoreCase);
            if (schema.IsPublicSchema)
            {
                foreach (var rule in policy.AllowedOperators)
                {
                    if (!schema.TryResolveQueryField(rule.Key, out var field))
                        throw new ArgumentException(
                            $"Operator permissions refer to an unknown public query field '{rule.Key}'.",
                            nameof(policy));
                    allowedByPath.Add(field.Identity, Array.AsReadOnly(rule.Value.ToArray()));
                }

                foreach (var field in schema.Fields)
                {
                    var hasFieldOperators = field.AllowedOperators != null;
                    var hasPolicyOperators = allowedByPath.TryGetValue(
                        field.Name,
                        out var policyOperators);
                    if (!hasFieldOperators && !hasPolicyOperators)
                        continue;

                    var effectiveOperators = hasFieldOperators
                        ? field.AllowedOperators
                        : policyOperators;
                    if (hasFieldOperators && hasPolicyOperators)
                        effectiveOperators = Array.AsReadOnly(
                            field.AllowedOperators.Intersect(policyOperators).ToArray());
                    allowedByPath[field.Name] = effectiveOperators;
                }

                return new ReadOnlyDictionary<string, IReadOnlyCollection<ComparisonOperator>>(
                    allowedByPath);
            }

            foreach (var rule in policy.AllowedOperators)
            {
                if (!schema.TryGetPolicyPathInfo(rule.Key, out var clrPath, out _, out _))
                {
                    throw new ArgumentException(
                        $"Operator permissions refer to an unknown entity property path '{rule.Key}'.",
                        nameof(policy));
                }

                clrPath = ExpressionSchema.NormalizePath(clrPath);
                if (allowedByPath.TryGetValue(clrPath, out var existing))
                {
                    allowedByPath[clrPath] = Array.AsReadOnly(existing
                        .Intersect(rule.Value)
                        .ToArray());
                }
                else
                {
                    allowedByPath.Add(clrPath, Array.AsReadOnly(rule.Value.ToArray()));
                }
            }

            return new ReadOnlyDictionary<string, IReadOnlyCollection<ComparisonOperator>>(allowedByPath);
        }

        private bool IsIncludedInAdditionalAllowlist(string sourcePath)
        {
            return _validProperties.Count == 0 ||
                _validProperties.Contains(
                    ExpressionSchema.NormalizePath(sourcePath),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static void ValidatePublicSchemaRestrictions(
            ExpressionSchema schema,
            QueryPolicy policy,
            IReadOnlyList<string> validProperties)
        {
            if (!schema.IsPublicSchema)
                return;

            foreach (var name in validProperties)
            {
                if (!schema.TryResolveQueryField(name, out var field) ||
                    !string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(
                        $"Additional allowed property '{name}' is not a registered public query field.",
                        nameof(validProperties));
            }

            foreach (var name in policy.AllowedOperators.Keys)
            {
                if (!schema.TryResolveQueryField(name, out var field) ||
                    !string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(
                        $"Operator permissions refer to an unknown public query field '{name}'.",
                        nameof(policy));
            }
        }
    }
}
