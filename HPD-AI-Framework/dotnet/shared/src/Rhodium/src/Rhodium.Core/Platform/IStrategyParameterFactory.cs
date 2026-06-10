namespace Rhodium.Platform;

public interface IStrategyParameterFactory<TStrategy>
    where TStrategy : Strategy
{
    static abstract TStrategy CreateVariant(ParameterSet parameters);
}
