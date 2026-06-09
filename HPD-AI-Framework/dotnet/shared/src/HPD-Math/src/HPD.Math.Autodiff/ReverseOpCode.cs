namespace HPD.Math.Autodiff;

/// <summary>
/// Operation code for one reverse-mode tape node.
/// </summary>
public enum ReverseOpCode : byte
{
    Input = 0,
    Constant,
    Add,
    Sub,
    Mul,
    Neg,
    Inv
}
