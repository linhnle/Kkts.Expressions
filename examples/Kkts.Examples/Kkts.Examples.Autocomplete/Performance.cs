using System.Diagnostics;
using System.Runtime.InteropServices;
using Kkts.Expressions;

internal static class Performance
{
    internal static int Run()
    {
#if DEBUG
        Console.Error.WriteLine("Run this workload with --configuration Release.");
        return 1;
#else
        const int warmups = 100;
        const int measured = 1000;
        var builder = new QuerySchema<SampleMetadata.Product>()
            .Field("Price", product => product.Price)
            .Field("Status", product => product.Status)
            .Field("CreatedAt", product => product.CreatedAt);
        for (var index = 0; index < 197; ++index)
            builder.Field("field" + index.ToString("D3"), product => product.Price);
        var schema = builder.Build();
        var variables = new ExpressionVariableSchema(new[]
        {
            new ExpressionVariableDefinition("utcnow", typeof(DateTime)),
            new ExpressionVariableDefinition("startOfMonth", typeof(DateTime))
        }.Concat(Enumerable.Range(0, 30).Select(index =>
            new ExpressionVariableDefinition("value" + index, typeof(decimal)))));
        var hints = new ExpressionValueSuggestionSchema(schema, new[]
        {
            new KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>>("Status",
                new[] { "Active", "Pending", "Closed" }.Concat(Enumerable.Range(0, 21).Select(index => "Choice" + index))
                    .Select(value => new ExpressionValueSuggestion(FilterValue.String(value))))
        });
        var context = new ExpressionQueryContext(schema, new QueryPolicy(maxExpressionLength: 4096));
        var workloads = new[]
        {
            "|", "P|", "Pri|", "Price |", "Status = |", "Status = 'Ac|ZZ'",
            "CreatedAt > $|", "CreatedAt > $ut|ZZ", "Price >|= 1 and Status = 'Active'",
            "Price = ) and Sta|", "not (Price > 1|",
            string.Concat(Enumerable.Repeat("Price > 1 and ", 200)) + "Sta|"
        }.Select(marked => (Text: marked.Replace("|", ""), Offset: marked.IndexOf('|'))).ToArray();
        var latencies = new double[measured];
        long allocated = 0;
        for (var index = -warmups; index < measured; ++index)
        {
            var workload = workloads[(index + warmups) % workloads.Length];
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            var result = context.CompleteExpression(workload.Text, workload.Offset, variables, hints);
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            GC.KeepAlive(result);
            if (index < 0) continue;
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            latencies[index] = elapsed;
        }
        Array.Sort(latencies);
        var p50 = latencies[(int)Math.Ceiling(measured * .50) - 1];
        var p95 = latencies[(int)Math.Ceiling(measured * .95) - 1];
        var meanAllocation = (double)allocated / measured;
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}; OS: {RuntimeInformation.OSDescription}; " +
            $"architecture: {RuntimeInformation.ProcessArchitecture}; CPUs: {Environment.ProcessorCount}");
        Console.WriteLine($"Warmups: {warmups}; measured: {measured}; snapshots: {workloads.Length}; " +
            $"maximum source UTF-16 units: {workloads.Max(item => item.Text.Length)}; descriptors: 256");
        Console.WriteLine(FormattableString.Invariant(
            $"p50: {p50:F3} ms; p95: {p95:F3} ms; mean allocation: {meanAllocation:F0} bytes/request"));
        var passed = p95 <= 20 && meanAllocation <= 256 * 1024;
        Console.WriteLine(passed ? "Representative thresholds: PASS" : "Representative thresholds: FAIL");
        return RunAdversarial() && passed ? 0 : 1;
#endif
    }

    private static bool RunAdversarial()
    {
        var schema = ExpressionSchema.FromType<SampleMetadata.Product>();
        var workloads = new[]
        {
            new string(' ', 16384), new string(' ', 16385), new string('!', 8192), new string('!', 8193),
            new string('(', 64), new string('(', 65), new string('!', 8000) + "Price > 1",
            "Status in ['Active', ", "Status = 'Active", "Price = ) and Status"
        };
        var passed = true;
        foreach (var text in workloads)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            var result = Interpreter.CompleteExpression(text, text.Length, schema);
            for (var iteration = 1; iteration < 10; ++iteration)
                GC.KeepAlive(Interpreter.CompleteExpression(text, text.Length, schema));
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 10;
            var allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / 10;
            if (text.Length == 16385 || text.Length == 8193 || text == new string('(', 65))
                passed &= result.Status == ExpressionCompletionStatus.LimitExceeded && result.Items.Count == 0;
            Console.WriteLine(FormattableString.Invariant(
                $"Adversarial source units: {text.Length}; repeats: 10; status: {result.Status}; mean: {elapsed:F3} ms; allocation: {allocated} bytes/request"));
        }
        var builder = new QuerySchema<SampleMetadata.Product>();
        for (var index = 0; index < 5000; ++index)
            builder.Field("field" + index.ToString("D4"), product => product.Price);
        var large = builder.Build();
        var largeBefore = GC.GetAllocatedBytesForCurrentThread();
        var largeStart = Stopwatch.GetTimestamp();
        var partial = Interpreter.CompleteExpression("", 0, large, options: new ExpressionCompletionOptions(200));
        passed &= partial.IsIncomplete && partial.Items.Count == 200 &&
            partial.Items.All(item => int.Parse(item.InsertionText.Substring(5)) < 4096);
        var repeated = Interpreter.CompleteExpression("", 0, large, options: new ExpressionCompletionOptions(200));
        passed &= partial.Items.Select(item => item.InsertionText).SequenceEqual(
            repeated.Items.Select(item => item.InsertionText));
        var largeElapsed = Stopwatch.GetElapsedTime(largeStart).TotalMilliseconds / 2;
        var largeAllocation = (GC.GetAllocatedBytesForCurrentThread() - largeBefore) / 2;
        Console.WriteLine($"Large metadata: 5000 fields; proven-prefix results: {partial.Items.Count}; " +
            $"incomplete: {partial.IsIncomplete}; adversarial checks: {(passed ? "PASS" : "FAIL")}");
        Console.WriteLine(FormattableString.Invariant(
            $"Large metadata mean: {largeElapsed:F3} ms; allocation: {largeAllocation} bytes/request; repeats: 2"));
        return passed;
    }
}
