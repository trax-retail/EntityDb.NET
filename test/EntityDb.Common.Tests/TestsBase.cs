﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EntityDb.Abstractions.Queries;
using EntityDb.Abstractions.Snapshots;
using EntityDb.Abstractions.Transactions;
using EntityDb.Common.Entities;
using EntityDb.Common.Extensions;
using EntityDb.Common.Projections;
using EntityDb.Common.Tests.Implementations.Entities;
using EntityDb.Common.Tests.Implementations.Projections;
using EntityDb.Common.Tests.Implementations.Snapshots;
using EntityDb.InMemory.Extensions;
using EntityDb.InMemory.Sessions;
using EntityDb.MongoDb.Provisioner.Extensions;
using EntityDb.MongoDb.Sessions;
using EntityDb.Redis.Extensions;
using EntityDb.Redis.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit.Abstractions;
using Xunit.DependencyInjection;
using Xunit.DependencyInjection.Logging;
using Xunit.Sdk;
using Pointer = EntityDb.Abstractions.ValueObjects.Pointer;

namespace EntityDb.Common.Tests;

public class TestsBase<TStartup>
    where TStartup : IStartup, new()
{
    public delegate void AddDependenciesDelegate(IServiceCollection serviceCollection);

    private static readonly TransactionsAdder[] AllTransactionAdders =
    {
        new("MongoDb", serviceCollection =>
        {
            serviceCollection.AddAutoProvisionMongoDbTransactions(true);

            serviceCollection.ConfigureAll<MongoDbTransactionSessionOptions>(options =>
            {
                options.ConnectionString = "mongodb://127.0.0.1:27017/?connect=direct&replicaSet=entitydb";
                options.DatabaseName = "Test";
                options.ReadTimeout = TimeSpan.FromSeconds(1);
                options.WriteTimeout = TimeSpan.FromSeconds(1);
            });

            serviceCollection.Configure<MongoDbTransactionSessionOptions>(TestSessionOptions.Write, options =>
            {
                options.ReadOnly = false;
            });

            serviceCollection.Configure<MongoDbTransactionSessionOptions>(TestSessionOptions.ReadOnly, options =>
            {
                options.ReadOnly = true;
                options.SecondaryPreferred = false;
            });

            serviceCollection.Configure<MongoDbTransactionSessionOptions>(TestSessionOptions.ReadOnlySecondaryPreferred, options =>
            {
                options.ReadOnly = true;
                options.SecondaryPreferred = true;
            });
        })
    };

    private readonly IConfiguration _configuration;
    private readonly ITest _test;
    private readonly ITestOutputHelperAccessor _testOutputHelperAccessor;

    protected TestsBase(IServiceProvider startupServiceProvider)
    {
        _configuration = startupServiceProvider.GetRequiredService<IConfiguration>();
        _testOutputHelperAccessor = startupServiceProvider.GetRequiredService<ITestOutputHelperAccessor>();
        _test =
            (typeof(TestOutputHelper).GetField("test", ~BindingFlags.Public)!.GetValue(_testOutputHelperAccessor.Output)
                as ITest).ShouldNotBeNull();
    }

    protected Task RunGenericTestAsync(Type[] typeArguments, object?[] invokeParameters)
    {
        var methodName = $"Generic_{new StackTrace().GetFrame(1)?.GetMethod()?.Name}";

        var methodOutput = GetType()
            .GetMethod(methodName, ~BindingFlags.Public)?
            .MakeGenericMethod(typeArguments)
            .Invoke(this, invokeParameters);

        return methodOutput
            .ShouldBeAssignableTo<Task>()
            .ShouldNotBeNull();
    }

    private static SnapshotAdder RedisSnapshotAdder<TSnapshot>()
        where TSnapshot : ISnapshotWithTestLogic<TSnapshot>
    {
        return new SnapshotAdder($"Redis<{typeof(TSnapshot).Name}>", typeof(TSnapshot), serviceCollection =>
        {
            serviceCollection.AddRedisSnapshots<TSnapshot>(true);

            serviceCollection.ConfigureAll<RedisSnapshotSessionOptions<TSnapshot>>(options =>
            {
                options.ConnectionString = "127.0.0.1:6379";
                options.KeyNamespace = TSnapshot.RedisKeyNamespace;
            });

            serviceCollection.Configure<RedisSnapshotSessionOptions<TSnapshot>>(TestSessionOptions.Write, options =>
            {
                options.ReadOnly = false;
            });

            serviceCollection.Configure<RedisSnapshotSessionOptions<TSnapshot>>(TestSessionOptions.ReadOnly, options =>
            {
                options.ReadOnly = true;
                options.SecondaryPreferred = false;
            });

            serviceCollection.Configure<RedisSnapshotSessionOptions<TSnapshot>>(TestSessionOptions.ReadOnlySecondaryPreferred, options =>
            {
                options.ReadOnly = true;
                options.SecondaryPreferred = true;
            });
        });
    }

    private static SnapshotAdder InMemorySnapshotAdder<TSnapshot>()
        where TSnapshot : ISnapshotWithTestLogic<TSnapshot>
    {
        return new SnapshotAdder($"InMemory<{typeof(TSnapshot).Name}>", typeof(TSnapshot), serviceCollection =>
        {
            serviceCollection.AddInMemorySnapshots<TSnapshot>(true);

            serviceCollection.Configure<InMemorySnapshotSessionOptions>(TestSessionOptions.Write, options =>
            {
                options.ReadOnly = false;
            });

            serviceCollection.Configure<InMemorySnapshotSessionOptions>(TestSessionOptions.ReadOnly, options =>
            {
                options.ReadOnly = true;
            });

            serviceCollection.Configure<InMemorySnapshotSessionOptions>(TestSessionOptions.ReadOnlySecondaryPreferred, options =>
            {
                options.ReadOnly = true;
            });
        });
    }

    private static IEnumerable<SnapshotAdder> AllSnapshotAdders<TSnapshot>()
        where TSnapshot : ISnapshotWithTestLogic<TSnapshot>
    {
        yield return RedisSnapshotAdder<TSnapshot>();
        yield return InMemorySnapshotAdder<TSnapshot>();
    }

    private static EntityAdder GetEntityAdder<TEntity>()
        where TEntity : IEntity<TEntity>
    {
        return new EntityAdder(typeof(TEntity).Name, typeof(TEntity),
            serviceCollection => { serviceCollection.AddEntity<TEntity>(); });
    }

    private static IEnumerable<SnapshotAdder> AllEntitySnapshotAdders<TEntity>()
        where TEntity : IEntity<TEntity>, ISnapshotWithTestLogic<TEntity>
    {
        return
            from snapshotAdder in AllSnapshotAdders<TEntity>()
            let entityAdder = GetEntityAdder<TEntity>()
            select new SnapshotAdder(snapshotAdder.Name, snapshotAdder.SnapshotType,
            snapshotAdder.AddDependencies + entityAdder.AddDependencies + (serviceCollection =>
            {
                serviceCollection.AddEntitySnapshotTransactionSubscriber<TEntity>(TestSessionOptions.ReadOnly,
                    TestSessionOptions.Write, true);
            }));
    }

    private static IEnumerable<EntityAdder> AllEntityAdders()
    {
        yield return GetEntityAdder<TestEntity>();
    }

    private static IEnumerable<SnapshotAdder> AllEntitySnapshotAdders()
    {
        return Enumerable.Empty<SnapshotAdder>()
            .Concat(AllEntitySnapshotAdders<TestEntity>());
    }

    private static IEnumerable<SnapshotAdder> AllProjectionAdders<TProjection>()
        where TProjection : IProjection<TProjection>, ISnapshotWithTestLogic<TProjection>
    {
        return AllSnapshotAdders<TProjection>()
            .Select(snapshotAdder => new SnapshotAdder(snapshotAdder.Name, snapshotAdder.SnapshotType,
                snapshotAdder.AddDependencies + (serviceCollection =>
                {
                    serviceCollection.AddProjection<TProjection>();
                    serviceCollection.AddProjectionSnapshotTransactionSubscriber<TProjection>(
                        TestSessionOptions.ReadOnly, TestSessionOptions.Write, true);
                }))
            );
    }

    private static IEnumerable<SnapshotAdder> AllProjectionSnapshotAdders()
    {
        return Enumerable.Empty<SnapshotAdder>()
            .Concat(AllProjectionAdders<OneToOneProjection>());
    }

    public static IEnumerable<object[]> AddTransactionsAndEntity()
    {
        return from transactionAdder in AllTransactionAdders
               from entityAdder in AllEntityAdders()
               select new object[] { transactionAdder, entityAdder };
    }

    public static IEnumerable<object[]> AddEntity()
    {
        return from entityAdder in AllEntityAdders()
               select new object[] { entityAdder };
    }

    public static IEnumerable<object[]> AddEntitySnapshots()
    {
        return from entitySnapshotAdder in AllEntitySnapshotAdders()
               select new object[] { entitySnapshotAdder };
    }

    public static IEnumerable<object[]> AddProjectionSnapshots()
    {
        return from projectionSnapshotAdder in AllProjectionSnapshotAdders()
               select new object[] { projectionSnapshotAdder };
    }

    public static IEnumerable<object[]> AddTransactionsAndEntitySnapshots()
    {
        return from transactionAdder in AllTransactionAdders
               from entitySnapshotAdder in AllEntitySnapshotAdders()
               select new object[] { transactionAdder, entitySnapshotAdder };
    }

    public static IEnumerable<object[]> AddTransactionsEntitySnapshotsAndProjectionSnapshots()
    {
        return from transactionAdder in AllTransactionAdders
               from entitySnapshotAdder in AllEntitySnapshotAdders()
               from projectionSnapshotAdder in AllProjectionSnapshotAdders()
               select new object[] { transactionAdder, entitySnapshotAdder, projectionSnapshotAdder };
    }

    protected IServiceScope CreateServiceScope(Action<IServiceCollection>? configureServices = null)
    {
        var serviceCollection = new ServiceCollection();

        var startup = new TStartup();

        serviceCollection.AddSingleton(_configuration);
        serviceCollection.AddSingleton(_test);

        serviceCollection.AddLogging(loggingBuilder =>
        {
            loggingBuilder.AddProvider(new XunitTestOutputLoggerProvider(_testOutputHelperAccessor));
            loggingBuilder.AddDebug();
            loggingBuilder.AddSimpleConsole(options => { options.IncludeScopes = true; });
        });

        startup.AddServices(serviceCollection);

        configureServices?.Invoke(serviceCollection);

        serviceCollection.AddSingleton(typeof(ILogger<>), typeof(TestLogger<>));

        var singletonServiceProvider = serviceCollection.BuildServiceProvider();

        var serviceScopeFactory = singletonServiceProvider.GetRequiredService<IServiceScopeFactory>();

        return new TestServiceScope(singletonServiceProvider, serviceScopeFactory.CreateScope());
    }

    protected static (ILoggerFactory Logger, Action<Times> LoggerVerifier) GetMockedLoggerFactory<TException>()
        where TException : Exception
    {
        var disposableMock = new Mock<IDisposable>(MockBehavior.Strict);

        disposableMock.Setup(disposable => disposable.Dispose());

        var loggerMock = new Mock<ILogger>(MockBehavior.Strict);

        loggerMock
            .Setup(logger => logger.BeginScope(It.IsAny<It.IsAnyType>()))
            .Returns(disposableMock.Object);

        loggerMock
            .Setup(logger => logger.Log
            (
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<TException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ));

        loggerMock
            .Setup(logger => logger.Log
            (
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<TException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ))
            .Verifiable();

        var loggerFactoryMock = new Mock<ILoggerFactory>(MockBehavior.Strict);

        loggerFactoryMock
            .Setup(factory => factory.CreateLogger(It.IsAny<string>()))
            .Returns(loggerMock.Object);

        loggerFactoryMock
            .Setup(factory => factory.AddProvider(It.IsAny<ILoggerProvider>()));

        void Verifier(Times times)
        {
            loggerMock
                .Verify
                (
                    logger => logger.Log
                    (
                        LogLevel.Error,
                        It.IsAny<EventId>(),
                        It.IsAny<It.IsAnyType>(),
                        It.IsAny<TException>(),
                        It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                    ),
                    times
                );
        }

        return (loggerFactoryMock.Object, Verifier);
    }

    protected static ITransactionRepositoryFactory GetMockedTransactionRepositoryFactory(
        object[]? commands = null)
    {
        commands ??= Array.Empty<object>();

        var transactionRepositoryMock = new Mock<ITransactionRepository>(MockBehavior.Strict);

        transactionRepositoryMock
            .Setup(repository => repository.PutTransaction(It.IsAny<ITransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        transactionRepositoryMock
            .Setup(repository => repository.GetCommands(It.IsAny<ICommandQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(commands);

        transactionRepositoryMock
            .Setup(repository => repository.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        var transactionRepositoryFactoryMock =
            new Mock<ITransactionRepositoryFactory>(MockBehavior.Strict);

        transactionRepositoryFactoryMock
            .Setup(factory => factory.CreateRepository(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactionRepositoryMock.Object);

        transactionRepositoryFactoryMock
            .Setup(factory => factory.Dispose());

        return transactionRepositoryFactoryMock.Object;
    }

    protected static ISnapshotRepositoryFactory<TEntity> GetMockedSnapshotRepositoryFactory<TEntity>
    (
        TEntity? snapshot = default
    )
    {
        var snapshotRepositoryMock = new Mock<ISnapshotRepository<TEntity>>(MockBehavior.Strict);

        snapshotRepositoryMock
            .Setup(repository => repository.GetSnapshotOrDefault(It.IsAny<Pointer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        snapshotRepositoryMock
            .Setup(repository => repository.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        var snapshotRepositoryFactoryMock = new Mock<ISnapshotRepositoryFactory<TEntity>>(MockBehavior.Strict);

        snapshotRepositoryFactoryMock
            .Setup(factory => factory.CreateRepository(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshotRepositoryMock.Object);

        snapshotRepositoryFactoryMock
            .Setup(factory => factory.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        return snapshotRepositoryFactoryMock.Object;
    }

    private record TestServiceScope
        (ServiceProvider SingletonServiceProvider, IServiceScope ServiceScope) : IServiceScope
    {
        public IServiceProvider ServiceProvider => ServiceScope.ServiceProvider;

        public void Dispose()
        {
            ServiceScope.Dispose();

            SingletonServiceProvider.Dispose();
        }
    }

    public record TransactionsAdder(string Name, AddDependenciesDelegate AddDependencies)
    {
        public override string ToString()
        {
            return Name;
        }
    }

    public record SnapshotAdder(string Name, Type SnapshotType, AddDependenciesDelegate AddDependencies)
    {
        public override string ToString()
        {
            return Name;
        }
    }

    public record EntityAdder(string Name, Type EntityType, AddDependenciesDelegate AddDependencies)
    {
        public override string ToString()
        {
            return Name;
        }
    }
}