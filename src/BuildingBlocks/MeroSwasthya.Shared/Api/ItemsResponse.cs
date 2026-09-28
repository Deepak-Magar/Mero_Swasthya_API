namespace MeroSwasthya.Shared.Api;

/// <summary>A.1: lists are always wrapped — <c>data: { items: [...] }</c>, never a bare array.</summary>
public sealed record ItemsResponse<T>(IReadOnlyList<T> Items);
