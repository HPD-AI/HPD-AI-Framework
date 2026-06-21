namespace HPD.ML.Backends.Mlx.Training;

public interface IMlxOptimizer
{
    void Step(MlxFloatTensor parameter, MlxFloatTensor gradient);
}
