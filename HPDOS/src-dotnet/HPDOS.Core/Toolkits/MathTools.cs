using HPD.Agent;

[Collapse("A toolkit providing basic mathematical operations")]
public class MathToolkit
{
    [AIFunction]
    [AIDescription("Adds two numbers and returns the sum.")]
    [RequiresPermission]
    public decimal Add(
        [AIDescription("First addend.")] decimal a,
        [AIDescription("Second addend.")] decimal b)
        => a + b;

    [AIFunction]
    [AIDescription("Multiplies two numbers and returns the product.")]
    public long Multiply(
        [AIDescription("First factor.")] long a,
        [AIDescription("Second factor.")] long b)
        => a * b;

    [AIFunction]
    [ConditionalFunction("AllowNegative == false")]
    [AIDescription("Returns the absolute value. Only available if negatives are not allowed.")]
    public long Abs(
        [AIDescription("Input value.")] long value)
        => Math.Abs(value);

    [AIFunction]
    [ConditionalFunction("MaxValue > 1000")]
    [AIDescription("Squares a number. Only available if maxValue > 1000.")]
    public long Square(
        [AIDescription("Input value.")] long value)
        => value * value;

    [AIFunction]
    [ConditionalFunction("AllowNegative == true")]
    [AIDescription("Subtracts b from a. Only available if negatives are allowed.")]
    public long Subtract(
        [AIDescription("Minuend.")] long a,
        [AIDescription("Subtrahend.")] long b)
        => a - b;

    [AIFunction]
    [ConditionalFunction("MaxValue < 500")]
    [AIDescription("Returns the minimum of two numbers. Only available if maxValue < 500.")]
    public long Min(
        [AIDescription("First value.")] long a,
        [AIDescription("Second value.")] long b)
        => Math.Min(a, b);

}
