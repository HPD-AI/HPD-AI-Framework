namespace Helium.Finance.Options;

internal static class OptionInputValidation
{
    public static void ValidateRight(OptionRight right)
    {
        if (right is not (OptionRight.Call or OptionRight.Put))
            throw new ArgumentOutOfRangeException(nameof(right), right, "Unsupported option right.");
    }

    public static void ValidateExerciseStyle(ExerciseStyle exerciseStyle)
    {
        if (exerciseStyle is not (ExerciseStyle.European or ExerciseStyle.American))
            throw new ArgumentOutOfRangeException(nameof(exerciseStyle), exerciseStyle, "Unsupported exercise style.");
    }
}
