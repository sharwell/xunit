﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Xunit.Runner.Common;
using Xunit.Sdk;
using Xunit.v3;

public class XunitTestAssemblyRunnerTests
{
	public class GetTestFrameworkDisplayName
	{
		[Fact]
		public static async ValueTask IsXunit()
		{
			await using var runner = TestableXunitTestAssemblyRunner.Create();

			var result = runner.GetTestFrameworkDisplayName();

			Assert.StartsWith("xUnit.net ", result);
		}
	}

	public class GetTestFrameworkEnvironment
	{
		[Fact]
		public static async ValueTask Default()
		{
			await using var runner = TestableXunitTestAssemblyRunner.Create();

			var result = runner.GetTestFrameworkEnvironment();

			Assert.EndsWith($"[collection-per-class, parallel ({Environment.ProcessorCount} threads)]", result);
		}

		[Fact]
		public static async ValueTask Attribute_NonParallel()
		{
			var attribute = Mocks.CollectionBehaviorAttribute(disableTestParallelization: true);
			var assembly = Mocks.TestAssembly(new[] { attribute });
			await using var runner = TestableXunitTestAssemblyRunner.Create(assembly: assembly);

			var result = runner.GetTestFrameworkEnvironment();

			Assert.EndsWith("[collection-per-class, non-parallel]", result);
		}

		[Fact]
		public static async ValueTask Attribute_MaxThreads()
		{
			var attribute = Mocks.CollectionBehaviorAttribute(maxParallelThreads: 3);
			var assembly = Mocks.TestAssembly(new[] { attribute });
			await using var runner = TestableXunitTestAssemblyRunner.Create(assembly: assembly);

			var result = runner.GetTestFrameworkEnvironment();

			Assert.EndsWith("[collection-per-class, parallel (3 threads)]", result);
		}

		[Fact]
		public static async ValueTask Attribute_Unlimited()
		{
			var attribute = Mocks.CollectionBehaviorAttribute(maxParallelThreads: -1);
			var assembly = Mocks.TestAssembly(new[] { attribute });
			await using var runner = TestableXunitTestAssemblyRunner.Create(assembly: assembly);

			var result = runner.GetTestFrameworkEnvironment();

			Assert.EndsWith("[collection-per-class, parallel (unlimited threads)]", result);
		}

		[Theory]
		[InlineData(CollectionBehavior.CollectionPerAssembly, "collection-per-assembly")]
		[InlineData(CollectionBehavior.CollectionPerClass, "collection-per-class")]
		public static async ValueTask Attribute_CollectionBehavior(CollectionBehavior behavior, string expectedDisplayText)
		{
			var attribute = Mocks.CollectionBehaviorAttribute(behavior, disableTestParallelization: true);
			var assembly = Mocks.TestAssembly(new[] { attribute });
			await using var runner = TestableXunitTestAssemblyRunner.Create(assembly: assembly);

			var result = runner.GetTestFrameworkEnvironment();

			Assert.EndsWith($"[{expectedDisplayText}, non-parallel]", result);
		}

		[Fact]
		public static async ValueTask Attribute_CustomCollectionFactory()
		{
			var factoryType = typeof(MyTestCollectionFactory);
			var attr = Mocks.CollectionBehaviorAttribute(factoryType.FullName!, factoryType.Assembly.FullName!, disableTestParallelization: true);
			var assembly = Mocks.TestAssembly(new[] { attr });
			await using var runner = TestableXunitTestAssemblyRunner.Create(assembly: assembly);

			var result = runner.GetTestFrameworkEnvironment();

			Assert.EndsWith("[My Factory, non-parallel]", result);
		}

		class MyTestCollectionFactory : IXunitTestCollectionFactory
		{
			public string DisplayName { get { return "My Factory"; } }

			public MyTestCollectionFactory(ITestAssembly assembly) { }

			public ITestCollection Get(ITypeInfo testClass)
			{
				throw new NotImplementedException();
			}
		}

		[Fact]
		public static async ValueTask TestOptions_NonParallel()
		{
			var options = _TestFrameworkOptions.ForExecution();
			options.SetDisableParallelization(true);
			await using var runner = TestableXunitTestAssemblyRunner.Create(executionOptions: options);

			var result = runner.GetTestFrameworkEnvironment();

			Assert.EndsWith("[collection-per-class, non-parallel]", result);
		}

		[Fact]
		public static async ValueTask TestOptions_MaxThreads()
		{
			var options = _TestFrameworkOptions.ForExecution();
			options.SetMaxParallelThreads(3);
			await using var runner = TestableXunitTestAssemblyRunner.Create(executionOptions: options);

			var result = runner.GetTestFrameworkEnvironment();

			Assert.EndsWith("[collection-per-class, parallel (3 threads)]", result);
		}

		[Fact]
		public static async ValueTask TestOptions_Unlimited()
		{
			var options = _TestFrameworkOptions.ForExecution();
			options.SetMaxParallelThreads(-1);
			await using var runner = TestableXunitTestAssemblyRunner.Create(executionOptions: options);

			var result = runner.GetTestFrameworkEnvironment();

			Assert.EndsWith("[collection-per-class, parallel (unlimited threads)]", result);
		}

		[Fact]
		public static async ValueTask TestOptionsOverrideAttribute()
		{
			var attribute = Mocks.CollectionBehaviorAttribute(disableTestParallelization: true, maxParallelThreads: 127);
			var options = _TestFrameworkOptions.ForExecution();
			options.SetDisableParallelization(false);
			options.SetMaxParallelThreads(3);
			var assembly = Mocks.TestAssembly(new[] { attribute });
			await using var runner = TestableXunitTestAssemblyRunner.Create(assembly: assembly, executionOptions: options);

			var result = runner.GetTestFrameworkEnvironment();

			Assert.EndsWith("[collection-per-class, parallel (3 threads)]", result);
		}
	}

	public class RunAsync
	{
		[Fact]
		public static async ValueTask Parallel_SingleThread()
		{
			var passing = Mocks.XunitTestCase<ClassUnderTest>("Passing");
			var other = Mocks.XunitTestCase<ClassUnderTest>("Other");
			var options = _TestFrameworkOptions.ForExecution();
			options.SetMaxParallelThreads(1);
			await using var runner = TestableXunitTestAssemblyRunner.Create(testCases: new[] { passing, other }, executionOptions: options);

			await runner.RunAsync();

			var threadIDs = runner.TestCasesRun.Select(x => x.Item1).ToList();
			Assert.Equal(threadIDs[0], threadIDs[1]);
		}

		[Fact]
		public static async ValueTask NonParallel()
		{
			var passing = Mocks.XunitTestCase<ClassUnderTest>("Passing");
			var other = Mocks.XunitTestCase<ClassUnderTest>("Other");
			var options = _TestFrameworkOptions.ForExecution();
			options.SetDisableParallelization(true);
			await using var runner = TestableXunitTestAssemblyRunner.Create(testCases: new[] { passing, other }, executionOptions: options);

			await runner.RunAsync();

			var threadIDs = runner.TestCasesRun.Select(x => x.Item1).ToList();
			Assert.Equal(threadIDs[0], threadIDs[1]);
		}
	}

	public class TestCaseOrderer
	{
		[Fact]
		public static async ValueTask CanSetTestCaseOrdererInAssemblyAttribute()
		{
			var ordererAttribute = Mocks.TestCaseOrdererAttribute<MyTestCaseOrderer>();
			var assembly = Mocks.TestAssembly(new[] { ordererAttribute });
			await using var runner = TestableXunitTestAssemblyRunner.Create(assembly: assembly);

			runner.Initialize();

			Assert.IsType<MyTestCaseOrderer>(runner.TestCaseOrderer);
		}

		class MyTestCaseOrderer : ITestCaseOrderer
		{
			public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases) where TTestCase : ITestCase
			{
				throw new NotImplementedException();
			}
		}

		[Fact]
		public static async ValueTask SettingUnknownTestCaseOrderLogsDiagnosticMessage()
		{
			var ordererAttribute = Mocks.TestCaseOrdererAttribute("UnknownType", "UnknownAssembly");
			var assembly = Mocks.TestAssembly(new[] { ordererAttribute });
			await using var runner = TestableXunitTestAssemblyRunner.Create(assembly: assembly);

			runner.Initialize();

			Assert.IsType<DefaultTestCaseOrderer>(runner.TestCaseOrderer);
			var diagnosticMessage = Assert.Single(runner.DiagnosticMessages.Cast<IDiagnosticMessage>());
			Assert.Equal("Could not find type 'UnknownType' in UnknownAssembly for assembly-level test case orderer", diagnosticMessage.Message);
		}

		[CulturedFact("en-US")]
		public static async ValueTask SettingTestCaseOrdererWithThrowingConstructorLogsDiagnosticMessage()
		{
			var ordererAttribute = Mocks.TestCaseOrdererAttribute<MyCtorThrowingTestCaseOrderer>();
			var assembly = Mocks.TestAssembly(new[] { ordererAttribute });
			await using var runner = TestableXunitTestAssemblyRunner.Create(assembly: assembly);

			runner.Initialize();

			Assert.IsType<DefaultTestCaseOrderer>(runner.TestCaseOrderer);
			var diagnosticMessage = Assert.Single(runner.DiagnosticMessages.Cast<IDiagnosticMessage>());
			Assert.StartsWith("Assembly-level test case orderer 'XunitTestAssemblyRunnerTests+TestCaseOrderer+MyCtorThrowingTestCaseOrderer' threw 'System.DivideByZeroException' during construction: Attempted to divide by zero.", diagnosticMessage.Message);
		}

		class MyCtorThrowingTestCaseOrderer : ITestCaseOrderer
		{
			public MyCtorThrowingTestCaseOrderer()
			{
				throw new DivideByZeroException();
			}

			public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases) where TTestCase : ITestCase
			{
				return Enumerable.Empty<TTestCase>();
			}
		}
	}

	public class TestCollectionOrderer
	{
		[Fact]
		public static async ValueTask CanSetTestCollectionOrdererInAssemblyAttribute()
		{
			var ordererAttribute = Mocks.TestCollectionOrdererAttribute<MyTestCollectionOrderer>();
			var assembly = Mocks.TestAssembly(new[] { ordererAttribute });
			await using var runner = TestableXunitTestAssemblyRunner.Create(assembly: assembly);

			runner.Initialize();

			Assert.IsType<MyTestCollectionOrderer>(runner.TestCollectionOrderer);
		}

		class MyTestCollectionOrderer : ITestCollectionOrderer
		{
			public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> TestCollections)
			{
				return TestCollections.OrderByDescending(c => c.DisplayName);
			}
		}

		[Fact]
		public static async ValueTask SettingUnknownTestCollectionOrderLogsDiagnosticMessage()
		{
			var ordererAttribute = Mocks.TestCollectionOrdererAttribute("UnknownType", "UnknownAssembly");
			var assembly = Mocks.TestAssembly(new[] { ordererAttribute });
			await using var runner = TestableXunitTestAssemblyRunner.Create(assembly: assembly);

			runner.Initialize();

			Assert.IsType<DefaultTestCollectionOrderer>(runner.TestCollectionOrderer);
			var diagnosticMessage = Assert.Single(runner.DiagnosticMessages.Cast<IDiagnosticMessage>());
			Assert.Equal("Could not find type 'UnknownType' in UnknownAssembly for assembly-level test collection orderer", diagnosticMessage.Message);
		}

		[CulturedFact("en-US")]
		public static async ValueTask SettingTestCollectionOrdererWithThrowingConstructorLogsDiagnosticMessage()
		{
			var ordererAttribute = Mocks.TestCollectionOrdererAttribute<MyCtorThrowingTestCollectionOrderer>();
			var assembly = Mocks.TestAssembly(new[] { ordererAttribute });
			await using var runner = TestableXunitTestAssemblyRunner.Create(assembly: assembly);

			runner.Initialize();

			Assert.IsType<DefaultTestCaseOrderer>(runner.TestCaseOrderer);
			var diagnosticMessage = Assert.Single(runner.DiagnosticMessages.Cast<IDiagnosticMessage>());
			Assert.StartsWith("Assembly-level test collection orderer 'XunitTestAssemblyRunnerTests+TestCollectionOrderer+MyCtorThrowingTestCollectionOrderer' threw 'System.DivideByZeroException' during construction: Attempted to divide by zero.", diagnosticMessage.Message);
		}

		class MyCtorThrowingTestCollectionOrderer : ITestCollectionOrderer
		{
			public MyCtorThrowingTestCollectionOrderer()
			{
				throw new DivideByZeroException();
			}

			public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
			{
				return Enumerable.Empty<ITestCollection>();
			}
		}
	}

	class ClassUnderTest
	{
		[Fact]
		public void Passing() { Thread.Sleep(0); }

		[Fact]
		public void Other() { Thread.Sleep(0); }
	}

	class TestableXunitTestAssemblyRunner : XunitTestAssemblyRunner
	{
		public List<IMessageSinkMessage> DiagnosticMessages;

		public ConcurrentBag<Tuple<int, IEnumerable<IXunitTestCase>>> TestCasesRun = new ConcurrentBag<Tuple<int, IEnumerable<IXunitTestCase>>>();

		TestableXunitTestAssemblyRunner(
			ITestAssembly testAssembly,
			IEnumerable<IXunitTestCase> testCases,
			List<IMessageSinkMessage> diagnosticMessages,
			_IMessageSink executionMessageSink,
			_ITestFrameworkExecutionOptions executionOptions)
				: base(testAssembly, testCases, SpyMessageSink.Create(messages: diagnosticMessages), executionMessageSink, executionOptions)
		{
			DiagnosticMessages = diagnosticMessages;
		}

		public static TestableXunitTestAssemblyRunner Create(
			ITestAssembly? assembly = null,
			IXunitTestCase[]? testCases = null,
			_ITestFrameworkExecutionOptions? executionOptions = null)
		{
			if (testCases == null)
				testCases = new[] { Mocks.XunitTestCase<ClassUnderTest>("Passing") };

			return new TestableXunitTestAssemblyRunner(
				assembly ?? testCases.First().TestMethod.TestClass.TestCollection.TestAssembly,
				testCases ?? new IXunitTestCase[0],
				new List<IMessageSinkMessage>(),
				SpyMessageSink.Create(),
				executionOptions ?? _TestFrameworkOptions.ForExecution()
			);
		}

		public new ITestCaseOrderer TestCaseOrderer
		{
			get { return base.TestCaseOrderer; }
		}

		public new ITestCollectionOrderer TestCollectionOrderer
		{
			get { return base.TestCollectionOrderer; }
			set { base.TestCollectionOrderer = value; }
		}

		public new string GetTestFrameworkDisplayName()
		{
			return base.GetTestFrameworkDisplayName();
		}

		public new string GetTestFrameworkEnvironment()
		{
			return base.GetTestFrameworkEnvironment();
		}

		public new void Initialize()
		{
			base.Initialize();
		}

		protected override Task<RunSummary> RunTestCollectionAsync(
			IMessageBus messageBus,
			ITestCollection testCollection,
			IEnumerable<IXunitTestCase> testCases,
			CancellationTokenSource cancellationTokenSource)
		{
			TestCasesRun.Add(Tuple.Create(Thread.CurrentThread.ManagedThreadId, testCases));
			Thread.Sleep(5); // Hold onto the worker thread long enough to ensure tests all get spread around
			return Task.FromResult(new RunSummary());
		}
	}
}
