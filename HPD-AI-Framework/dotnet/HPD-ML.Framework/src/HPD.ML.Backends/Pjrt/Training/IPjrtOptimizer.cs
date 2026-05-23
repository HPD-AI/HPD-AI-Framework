namespace HPD.ML.Backends.Pjrt.Training;

public interface IPjrtOptimizer
{
    void Step(PjrtFloatTensor parameter, PjrtFloatTensor gradient);
}
