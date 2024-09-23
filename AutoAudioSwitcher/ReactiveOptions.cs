// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace AutoAudioSwitcher;

/// <inheritdoc cref="BehaviorSubject{T}"/>
internal interface IBehaviorObservable<out T> : IObservable<T>
{
    /// <inheritdoc cref="BehaviorSubject{T}.Value"/>
    T Value { get; }
}

internal static class ReactiveOptionsExtensions
{
    /// <summary>
    /// Adds an <see cref="IBehaviorObservable{T}"/> and <see cref="IObservable{T}"/> to the DI container, with
    /// <typeparamref name="T"/> bound to the given configuration section.
    /// </summary>
    public static IServiceCollection ConfigureObservable<T>(
        this IServiceCollection services, IConfiguration config, Action<BinderOptions>? configureOptions = null)
        where T : new()
    {
        services.AddSingleton<IBehaviorObservable<T>>(_ => new ReactiveOptions<T>(config, configureOptions));
        services.AddSingleton<IObservable<T>>(provider => provider.GetRequiredService<IBehaviorObservable<T>>());
        return services;
    }
}

/// <summary>
/// An <see cref="IObservable{T}"/> alternative to the familiar options pattern.
/// </summary>
internal class ReactiveOptions<T> : IBehaviorObservable<T>, IDisposable where T : new()
{
    private readonly IConfiguration config;
    private readonly Action<BinderOptions>? configureOptions;
    private readonly BehaviorSubject<T> subject;
    private readonly IDisposable changeTokenSubscription;

    public ReactiveOptions(IConfiguration config, Action<BinderOptions>? configureOptions)
    {
        this.config = config;
        this.configureOptions = configureOptions;
        subject = new(GetCurrent());
        changeTokenSubscription = ChangeToken.OnChange(config.GetReloadToken, Reload);
    }

    public T Value => subject.Value;

    public IDisposable Subscribe(IObserver<T> observer) => subject.DistinctUntilChanged().Subscribe(observer);

    private void Reload()
    {
        subject.OnNext(GetCurrent());
    }

    private T GetCurrent()
    {
        T obj = new();
        config.Bind(obj, configureOptions);
        return obj;
    }

    public void Dispose()
    {
        changeTokenSubscription.Dispose();
    }
}
