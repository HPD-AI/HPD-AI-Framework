using System.Globalization;

namespace HPD.Base;

internal static class BaseQueryValue
{
    public static QueryValue From<T>(T value)
    {
        if (value is null)
        {
            return new QueryValue { Kind = QueryValueKind.Null };
        }

        object boxed = value;
        return boxed switch
        {
            string text => new QueryValue
            {
                Kind = QueryValueKind.String,
                String = text,
            },
            char character => new QueryValue
            {
                Kind = QueryValueKind.String,
                String = character.ToString(),
            },
            bool boolean => new QueryValue
            {
                Kind = QueryValueKind.Boolean,
                Boolean = boolean,
            },
            byte or sbyte or short or ushort or int or uint or long =>
                new QueryValue
                {
                    Kind = QueryValueKind.Integer,
                    Integer = Convert.ToInt64(boxed, CultureInfo.InvariantCulture),
                },
            ulong unsigned when unsigned <= long.MaxValue => new QueryValue
            {
                Kind = QueryValueKind.Integer,
                Integer = (long)unsigned,
            },
            float or double => new QueryValue
            {
                Kind = QueryValueKind.Number,
                Number = Convert.ToDouble(boxed, CultureInfo.InvariantCulture),
            },
            decimal number => new QueryValue
            {
                Kind = QueryValueKind.Decimal,
                Decimal = number.ToString(CultureInfo.InvariantCulture),
            },
            DateTimeOffset dateTime => new QueryValue
            {
                Kind = QueryValueKind.DateTime,
                DateTime = dateTime,
            },
            DateTime dateTime => new QueryValue
            {
                Kind = QueryValueKind.DateTime,
                DateTime = new DateTimeOffset(dateTime.ToUniversalTime()),
            },
            Guid id => new QueryValue
            {
                Kind = QueryValueKind.Id,
                Id = id.ToString("D"),
            },
            RecordId id => new QueryValue
            {
                Kind = QueryValueKind.Id,
                Id = id.Value,
            },
            _ when boxed.GetType().IsEnum => new QueryValue
            {
                Kind = QueryValueKind.String,
                String = boxed.ToString(),
            },
            _ => throw new ArgumentException(
                $"Type '{typeof(T).FullName}' is not a supported scalar query value.",
                nameof(value)),
        };
    }
}
