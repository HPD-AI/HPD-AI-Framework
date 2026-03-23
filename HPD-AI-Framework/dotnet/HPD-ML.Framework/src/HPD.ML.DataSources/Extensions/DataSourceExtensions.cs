namespace HPD.ML.DataSources;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// C# 14 extension members for data source discovery.
/// Enables: IDataHandle.LoadCsv("data.csv")
/// </summary>
public static class DataSourceExtensions
{
    extension(IDataHandle)
    {
        /// <summary>Load a CSV file with schema inference.</summary>
        public static IDataHandle LoadCsv(string path, CsvOptions? options = null)
            => CsvDataHandle.Create(path, options);

        /// <summary>Load a CSV file with explicit schema.</summary>
        public static IDataHandle LoadCsv(string path, Schema schema, CsvOptions? options = null)
            => CsvDataHandle.Create(path, schema, options);

        /// <summary>Load a JSON or JSONL file with schema inference.</summary>
        public static IDataHandle LoadJson(string path, JsonOptions? options = null)
            => JsonDataHandle.Create(path, options);

        /// <summary>Load an Apache Parquet file.</summary>
        public static IDataHandle LoadParquet(string path, ParquetOptions? options = null)
            => ParquetDataHandle.Create(path, options);

        /// <summary>Create from an enumerable with explicit schema and extractor.</summary>
        public static IDataHandle FromEnumerable<T>(
            IEnumerable<T> items,
            Schema schema,
            Func<T, Dictionary<string, object>> extractor)
            => EnumerableDataHandle.Create(items, schema, extractor);

        /// <summary>Create from dictionaries with schema inference.</summary>
        public static IDataHandle FromDictionaries(
            IEnumerable<IReadOnlyDictionary<string, object>> rows)
            => DictionaryDataHandle.Create(rows);

        /// <summary>Create from explicit rows (testing, small datasets).</summary>
        public static IDataHandle FromRows(
            Schema schema,
            params ReadOnlySpan<Dictionary<string, object>> rows)
            => RowsDataHandle.Create(schema, rows);
    }
}
