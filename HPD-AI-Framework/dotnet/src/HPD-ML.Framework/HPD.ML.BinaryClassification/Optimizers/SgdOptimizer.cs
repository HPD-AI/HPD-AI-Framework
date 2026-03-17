namespace HPD.ML.BinaryClassification;

using Helium.Algebra;
using Helium.Algorithms;
using Helium.Primitives;
using Double = Helium.Primitives.Double;

/// <summary>
/// Stochastic Gradient Descent with momentum.
/// Used by Averaged Perceptron and Linear SVM.
/// </summary>
internal sealed class SgdOptimizer
{
    private readonly double _learningRate;
    private readonly double _momentum;
    private readonly double _l2Regularization;
    private readonly bool _decreaseLearningRate;

    public SgdOptimizer(
        double learningRate = 0.1,
        double momentum = 0,
        double l2Regularization = 0,
        bool decreaseLearningRate = false)
    {
        _learningRate = learningRate;
        _momentum = momentum;
        _l2Regularization = l2Regularization;
        _decreaseLearningRate = decreaseLearningRate;
    }

    /// <summary>
    /// Run one SGD step: compute gradient on a single sample, update parameters.
    /// Returns updated parameters and loss value.
    /// </summary>
    public (Vector<Double> NewParams, double Loss) Step(
        Func<Vector<Var<Double>>, Var<Double>> sampleLoss,
        Vector<Double> parameters,
        Vector<Double> velocity,
        int iteration,
        out Vector<Double> newVelocity)
    {
        int n = parameters.Length;
        double lr = _decreaseLearningRate
            ? _learningRate / (1.0 + iteration * 0.01)
            : _learningRate;

        // Compute gradient via Helium autodiff
        var (lossValue, gradient) = Grad.ValueAndGrad(sampleLoss, parameters);

        // Add L2 regularization gradient
        if (_l2Regularization > 0)
        {
            var regGrad = new Double[n];
            for (int i = 0; i < n; i++)
                regGrad[i] = gradient[i] + new Double(_l2Regularization) * parameters[i];
            gradient = Vector<Double>.FromArray(regGrad);
        }

        // Momentum update
        var newVel = new Double[n];
        var newParams = new Double[n];
        for (int i = 0; i < n; i++)
        {
            newVel[i] = new Double(_momentum) * velocity[i] - new Double(lr) * gradient[i];
            newParams[i] = parameters[i] + newVel[i];
        }

        newVelocity = Vector<Double>.FromArray(newVel);
        return (Vector<Double>.FromArray(newParams), (double)lossValue);
    }
}
