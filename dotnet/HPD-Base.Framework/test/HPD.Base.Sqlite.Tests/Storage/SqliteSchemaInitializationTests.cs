using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteSchemaInitializationTests
{
    [Fact]
    public async Task DeclaredFieldsAndIndexesHaveStableTypedPhysicalStorage()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-typed-" + Guid.NewGuid().ToString("N") + ".db");
        var collection = Collection() with
        {
            SchemaMode = SchemaMode.Strict,
            UnknownFields = UnknownFieldPolicy.Reject,
            Fields =
            [
                StringField("item.title", "title", BaseFieldPresence.Required, BaseFieldNullability.NonNullable),
                ScalarField("item.rank", "rank", "integer", null, BaseScalarKind.Int64, BaseFieldPresence.Optional, BaseFieldNullability.Nullable),
                BinaryField("item.blob", "blob", 16),
            ],
            Indexes =
            [
                new BaseLogicalIndexDefinition
                {
                    Id = BaseLogicalIndexId.Create("item.by-rank"), Version = 1, CollectionId = "items", Unique = false, StoreRequired = false,
                    Parts = [new BaseLogicalIndexPart { FieldOrdinal = 1, Direction = BaseIndexSortDirection.Descending, Collation = BaseIndexCollation.OrdinalBinary, NullOrder = BaseIndexNullOrder.MissingThenNullThenValue }],
                    MembershipPredicate = TruePredicate(), Checksum = default
                }
            ]
        };
        try
        {
            await using var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path, Collections = [collection] });
            (await store.ListAsync(collection, new RecordQuery(), Operation(BaseOperationKind.List))).Status.Should().Be(OperationStatus.Ok);
            OperationResult<RecordEnvelope> created = await store.CreateAsync(collection, new RecordCreateRequest
            {
                RequestedId = RecordId.Create("binary"),
                Payload = Payload("{\"title\":\"schema\",\"blob\":\"AQID\"}")
            }, Operation(BaseOperationKind.Create));
            created.Status.Should().Be(OperationStatus.Created);
            created.Value!.Payload.Fields!["blob"].GetString().Should().Be("AQID");

            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();
            await using var columns = connection.CreateCommand();
            columns.CommandText = $"PRAGMA table_info({PhysicalTable("items")});";
            var physicalColumns = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var reader = await columns.ExecuteReaderAsync())
                while (await reader.ReadAsync()) physicalColumns[reader.GetString(1)] = reader.GetString(2);

            physicalColumns.Should().Contain(new KeyValuePair<string, string>(PhysicalField("item.title"), "TEXT"));
            physicalColumns.Should().Contain(new KeyValuePair<string, string>(PhysicalField("item.rank"), "INTEGER"));
            physicalColumns.Should().Contain(new KeyValuePair<string, string>(PhysicalField("item.blob"), "BLOB"));
            physicalColumns.Keys.Should().Contain(PhysicalPresence("item.rank"));
            physicalColumns.Keys.Should().NotContain(["payload_json", "extension_json"]);

            await using var indexes = connection.CreateCommand();
            indexes.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index';";
            var names = new List<string>();
            await using (var reader = await indexes.ExecuteReaderAsync())
                while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            names.Should().Contain(PhysicalIndex("item.by-rank"));
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task TestSchemaInitializationCreatesOnlyProviderOwnedTables()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-schema-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE host_table(id TEXT PRIMARY KEY);";
                await command.ExecuteNonQueryAsync();
            }

            var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path, SchemaPrefix = "l21_" });
            var create = await store.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = RecordId.Create("one"), Payload = Payload() }, Operation(BaseOperationKind.Create));
            create.Status.Should().Be(OperationStatus.Created);

            await using var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await verify.OpenAsync();
            await using var list = verify.CreateCommand();
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
            var names = new List<string>();
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));

            names.Should().Contain(["host_table", PhysicalTable("items"), "l21_collections", "l21_provider_state", "l21_mutation_journal"]);
            names.Should().NotContain("l21_records");
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task MissingAcceptedSchemaFailsClosedAndHealthIsUnhealthy()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-missing-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path }, initializeSchema: false);
            var result = await store.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = RecordId.Create("one"), Payload = Payload() }, Operation(BaseOperationKind.Create));

            result.Status.Should().Be(OperationStatus.StoreError);
            result.Error!.Code.Should().Be("sqlite.schema.missing");

            var services = new ServiceCollection().AddLogging().AddHPDBaseSqliteStore(options =>
            {
                options.DataSource = path;
            });
            await using var provider = services.BuildServiceProvider();
            var health = await provider.GetRequiredService<IEnumerable<IBaseHealthContributor>>().Single().GetHealthAsync();
            health.Single().Status.Should().Be(HealthStatus.Unhealthy);
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task ExistingProviderTableWithMissingColumnsFailsValidation()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-badschema-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE TABLE {PhysicalTable("items")}(record_id TEXT NOT NULL);";
                await command.ExecuteNonQueryAsync();
            }

            var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path }, initializeSchema: false);
            var result = await store.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = RecordId.Create("one"), Payload = Payload() }, Operation(BaseOperationKind.Create));

            result.Status.Should().Be(OperationStatus.StoreError);
            result.Error!.Code.Should().Be("sqlite.schema.missing");
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task ExistingMutationJournalWithMissingColumnFailsValidation()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "hpd-base-sqlite-badjournal-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var initialized = SqliteTestFactory.Create(
                new HPDBaseSqliteOptions { DataSource = path }))
            {
                var created = await initialized.CreateAsync(
                    Collection(),
                    new RecordCreateRequest
                    {
                        RequestedId = RecordId.Create("seed"),
                        Payload = Payload()
                    },
                    Operation(BaseOperationKind.Create));
                created.Status.Should().Be(OperationStatus.Created);
            }

            await using (var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "ALTER TABLE hpd_base_mutation_journal DROP COLUMN visibility;";
                await command.ExecuteNonQueryAsync();
            }

            await using var store = SqliteTestFactory.Create(
                new HPDBaseSqliteOptions { DataSource = path }, initializeSchema: false);
            var result = await store.CreateAsync(
                Collection(),
                new RecordCreateRequest
                {
                    RequestedId = RecordId.Create("after-corruption"),
                    Payload = Payload()
                },
                Operation(BaseOperationKind.Create));

            result.Status.Should().Be(OperationStatus.StoreError);
            result.Error!.Code.Should().Be("sqlite.schema.missing");
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
            {
                if (File.Exists(candidate))
                    File.Delete(candidate);
            }
        }
    }

    [Fact]
    public async Task ExistingPhysicalIndexWithWrongShapeIsReportedAsDrift()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-index-drift-" + Guid.NewGuid().ToString("N") + ".db");
        var collection = Collection() with
        {
            Fields = [ScalarField("item.rank", "rank", "integer", null, BaseScalarKind.Int64)],
            Indexes = [new BaseLogicalIndexDefinition
            {
                Id = BaseLogicalIndexId.Create("item.by-rank"), Version = 1, CollectionId = "items", Unique = false, StoreRequired = false,
                Parts = [new BaseLogicalIndexPart { FieldOrdinal = 0, Direction = BaseIndexSortDirection.Ascending, Collation = BaseIndexCollation.OrdinalBinary, NullOrder = BaseIndexNullOrder.MissingThenNullThenValue }],
                MembershipPredicate = TruePredicate(), Checksum = default
            }]
        };
        var options = new HPDBaseSqliteOptions { DataSource = path, Collections = [collection] };
        try
        {
            await using (SqliteRecordStore initialized = SqliteTestFactory.Create(options)) { }
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();
            await using (SqliteCommand corrupt = connection.CreateCommand())
            {
                corrupt.CommandText = $"DROP INDEX {PhysicalIndex("item.by-rank")}; CREATE INDEX {PhysicalIndex("item.by-rank")} ON {PhysicalTable("items")}(record_id);";
                await corrupt.ExecuteNonQueryAsync();
            }

            string[] drift = await new SqliteSchemaInitializer(options).GetMissingSchemaPartsAsync(connection, CancellationToken.None);
            drift.Should().Contain("index-shape:" + PhysicalIndex("item.by-rank"));
        }
        finally
        {
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task ExistingPhysicalIndexWithSubstitutedPredicateIsReportedAsContractDrift()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-index-contract-drift-" + Guid.NewGuid().ToString("N") + ".db");
        CollectionDefinition collection = Collection() with
        {
            Fields = [ScalarField("item.rank", "rank", "integer", null, BaseScalarKind.Int64)],
            Indexes = [new BaseLogicalIndexDefinition
            {
                Id = BaseLogicalIndexId.Create("item.by-rank"), Version = 1, CollectionId = "items", Unique = false, StoreRequired = false,
                Parts = [new BaseLogicalIndexPart { FieldOrdinal = 0, Direction = BaseIndexSortDirection.Ascending, Collation = BaseIndexCollation.OrdinalBinary, NullOrder = BaseIndexNullOrder.MissingThenNullThenValue }],
                MembershipPredicate = TruePredicate(), Checksum = default,
            }],
        };
        var options = new HPDBaseSqliteOptions { DataSource = path, Collections = [collection] };
        try
        {
            await using (SqliteRecordStore initialized = SqliteTestFactory.Create(options)) { }
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()); await connection.OpenAsync();
            await using (SqliteCommand corrupt = connection.CreateCommand())
            {
                corrupt.CommandText = $"DROP INDEX {PhysicalIndex("item.by-rank")}; CREATE INDEX {PhysicalIndex("item.by-rank")} ON {PhysicalTable("items")}({PhysicalRank("item.by-rank", 0)} ASC, {PhysicalOrder("item.by-rank", 0)} ASC, record_id ASC) WHERE 1=0;";
                await corrupt.ExecuteNonQueryAsync();
            }
            string[] drift = await new SqliteSchemaInitializer(options).GetMissingSchemaPartsAsync(connection, CancellationToken.None);
            drift.Should().Contain("index-contract:" + PhysicalIndex("item.by-rank"));
        }
        finally { foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate); }
    }

    [Fact]
    public async Task PartialUniqueIndexDistinguishesMissingNullAndPresentValues()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-partial-unique-" + Guid.NewGuid().ToString("N") + ".db");
        BaseIndexPredicateId defined = BaseIndexPredicateId.Create("defined"), notNull = BaseIndexPredicateId.Create("not-null"), root = BaseIndexPredicateId.Create("root");
        CollectionDefinition collection = Collection() with
        {
            Fields =
            [
                StringField("item.normalized", "normalized", BaseFieldPresence.Optional, BaseFieldNullability.Nullable),
                StringField("item.tenant", "tenant", BaseFieldPresence.Required, BaseFieldNullability.NonNullable),
            ],
            Indexes = [new BaseLogicalIndexDefinition
            {
                Id = BaseLogicalIndexId.Create("item.tenant-normalized"), Version = 1, CollectionId = "items", Unique = true, StoreRequired = true,
                Parts =
                [
                    new BaseLogicalIndexPart { FieldOrdinal = 1, Direction = BaseIndexSortDirection.Ascending, Collation = BaseIndexCollation.OrdinalBinary, NullOrder = BaseIndexNullOrder.MissingThenNullThenValue },
                    new BaseLogicalIndexPart { FieldOrdinal = 0, Direction = BaseIndexSortDirection.Ascending, Collation = BaseIndexCollation.OrdinalBinary, NullOrder = BaseIndexNullOrder.MissingThenNullThenValue },
                ],
                MembershipPredicate = new BaseIndexPredicateRegistry
                {
                    Root = root, Checksum = default,
                    Nodes =
                    [
                        new BaseIndexPredicateNode { Id = defined, Kind = BaseIndexPredicateNodeKind.IsDefined, FieldOrdinal = 0 },
                        new BaseIndexPredicateNode { Id = notNull, Kind = BaseIndexPredicateNodeKind.IsNotNull, FieldOrdinal = 0 },
                        new BaseIndexPredicateNode { Id = root, Kind = BaseIndexPredicateNodeKind.And, Children = [defined, notNull] },
                    ]
                }, Checksum = default,
            }]
        };
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path, Collections = [collection] });
            foreach ((string id, string json) in new[] { ("m1", "{\"tenant\":\"t\"}"), ("m2", "{\"tenant\":\"t\"}"), ("n1", "{\"tenant\":\"t\",\"normalized\":null}"), ("n2", "{\"tenant\":\"t\",\"normalized\":null}"), ("v1", "{\"tenant\":\"t\",\"normalized\":\"name\"}") })
            {
                OperationResult<RecordEnvelope> created = await store.CreateAsync(collection, new RecordCreateRequest { RequestedId = RecordId.Create(id), Payload = Payload(json) }, Operation(BaseOperationKind.Create));
                created.IsSuccess().Should().BeTrue(created.Error?.Code + ": " + created.Error?.Message);
            }
            OperationResult<RecordEnvelope> duplicate = await store.CreateAsync(collection, new RecordCreateRequest { RequestedId = RecordId.Create("v2"), Payload = Payload("{\"tenant\":\"t\",\"normalized\":\"name\"}") }, Operation(BaseOperationKind.Create));
            duplicate.Status.Should().Be(OperationStatus.Conflict);
            duplicate.Error!.Code.Should().Be(BaseSchemaErrorCodes.UniqueConstraintViolated);

            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()); await connection.OpenAsync(); await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT sql FROM sqlite_master WHERE type='index' AND name=$name;"; command.Parameters.AddWithValue("$name", PhysicalIndex("item.tenant-normalized"));
            string sql = (string)(await command.ExecuteScalarAsync())!; sql.Should().Contain(PhysicalPresence("item.normalized") + "=1").And.Contain(PhysicalField("item.normalized") + " IS NOT NULL");
        }
        finally { foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate); }
    }

    [Fact]
    public async Task EqualityOnlyCanonicalJsonCodecBacksUniqueEqualityWithoutOrderingShadows()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-json-unique-" + Guid.NewGuid().ToString("N") + ".db");
        BaseScalarCodecAuthority codec = BaseGeneratedSchemaRegistration.ScalarCodec(BaseScalarKind.CanonicalJson);
        var constraints = new BaseScalarConstraintSet { MaximumCanonicalJsonBytes = 256, MaximumJsonDepth = 4, MaximumJsonArrayItems = 8, MaximumJsonObjectProperties = 8, MaximumJsonTotalNodes = 16, MaximumJsonTotalStringUtf8Bytes = 64, MaximumJsonTotalNameUtf8Bytes = 64 };
        FieldDefinition field = new()
        {
            Id = "item.document", ApplicationName = "document", WireName = "document", Type = "object", Format = "base-json-v1",
            Presence = BaseFieldPresence.Required, Nullability = BaseFieldNullability.NonNullable, ScalarKind = BaseScalarKind.CanonicalJson,
            ScalarCodec = codec, ScalarConstraints = constraints,
            ScalarConstraintChecksum = BaseGeneratedSchemaRegistration.ScalarConstraintChecksum("items", "item.document", BaseFieldPresence.Required, BaseFieldNullability.NonNullable, codec, constraints),
        };
        CollectionDefinition collection = Collection() with
        {
            Fields = [field], Indexes = [BaseSchemaContract.SealIndex(new BaseLogicalIndexDefinition
            {
                Id = BaseLogicalIndexId.Create("item.document-unique"), Version = 1, CollectionId = "items", Unique = true, StoreRequired = true,
                Parts = [new BaseLogicalIndexPart { FieldOrdinal = 0, Direction = BaseIndexSortDirection.Ascending, Collation = BaseIndexCollation.OrdinalBinary, NullOrder = BaseIndexNullOrder.MissingThenNullThenValue }],
                MembershipPredicate = TruePredicate(), Checksum = default,
            }, [field])],
        };
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path, Collections = [collection] });
            (await store.CreateAsync(collection, new RecordCreateRequest { RequestedId = RecordId.Create("a"), Payload = Payload("{\"document\":{\"a\":1}}") }, Operation(BaseOperationKind.Create))).IsSuccess().Should().BeTrue();
            OperationResult<RecordEnvelope> duplicate = await store.CreateAsync(collection, new RecordCreateRequest { RequestedId = RecordId.Create("b"), Payload = Payload("{\"document\":{\"a\":1}}") }, Operation(BaseOperationKind.Create));
            duplicate.Status.Should().Be(OperationStatus.Conflict); duplicate.Error!.Code.Should().Be(BaseSchemaErrorCodes.UniqueConstraintViolated);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()); await connection.OpenAsync();
            await using SqliteCommand columns = connection.CreateCommand(); columns.CommandText = $"PRAGMA table_info({PhysicalTable("items")});";
            var names = new List<string>(); await using SqliteDataReader reader = await columns.ExecuteReaderAsync(); while (await reader.ReadAsync()) names.Add(reader.GetString(1));
            names.Should().NotContain(PhysicalRank("item.document-unique", 0)).And.NotContain(PhysicalOrder("item.document-unique", 0));
        }
        finally { foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate); }
    }

    [Theory]
    [InlineData("{\"tenant\":\"t\"}", BaseIndexPredicateNodeKind.IsMissing)]
    [InlineData("{\"tenant\":\"t\",\"value\":null}", BaseIndexPredicateNodeKind.IsNull)]
    public async Task UniqueEqualityAuthorityDoesNotCollapseIncludedMissingOrNull(string json, BaseIndexPredicateNodeKind membership)
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-state-unique-" + Guid.NewGuid().ToString("N") + ".db");
        BaseIndexPredicateId root = BaseIndexPredicateId.Create("root");
        CollectionDefinition collection = Collection() with
        {
            Fields =
            [
                StringField("item.value", "value", BaseFieldPresence.Optional, BaseFieldNullability.Nullable),
                StringField("item.tenant", "tenant", BaseFieldPresence.Required, BaseFieldNullability.NonNullable),
            ],
            Indexes = [new BaseLogicalIndexDefinition
            {
                Id = BaseLogicalIndexId.Create("item.state-unique"), Version = 1, CollectionId = "items", Unique = true, StoreRequired = true,
                Parts = [new BaseLogicalIndexPart { FieldOrdinal = 1, Direction = BaseIndexSortDirection.Ascending, Collation = BaseIndexCollation.OrdinalBinary, NullOrder = BaseIndexNullOrder.MissingThenNullThenValue }],
                MembershipPredicate = new BaseIndexPredicateRegistry
                {
                    Root = root, Checksum = default,
                    Nodes = [new BaseIndexPredicateNode { Id = root, Kind = membership, FieldOrdinal = 1 }],
                },
                Checksum = default,
            }],
        };
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path, Collections = [collection] });
            OperationResult<RecordEnvelope> first = await store.CreateAsync(collection, new RecordCreateRequest { RequestedId = RecordId.Create("first"), Payload = Payload(json) }, Operation(BaseOperationKind.Create));
            first.IsSuccess().Should().BeTrue(first.Error?.Code + ": " + first.Error?.Message);
            OperationResult<RecordEnvelope> duplicate = await store.CreateAsync(collection, new RecordCreateRequest { RequestedId = RecordId.Create("second"), Payload = Payload(json) }, Operation(BaseOperationKind.Create));
            duplicate.Status.Should().Be(OperationStatus.Conflict);
            duplicate.Error!.Code.Should().Be(BaseSchemaErrorCodes.UniqueConstraintViolated);
        }
        finally { foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate); }
    }

    [Fact]
    public async Task ExactDecimalAndUInt64StorageRoundTripsWithoutProviderNarrowing()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-exact-scalars-" + Guid.NewGuid().ToString("N") + ".db");
        CollectionDefinition collection = Collection() with
        {
            Fields =
            [
                ScalarField("item.amount", "amount", "number", "decimal", BaseScalarKind.Decimal),
                ScalarField("item.sequence", "sequence", "integer", null, BaseScalarKind.UInt64),
                ScalarField("item.instant", "instant", "string", "date-time", BaseScalarKind.UtcDateTime),
            ],
        };
        const string json = "{\"amount\":170141183460469231731687303715884105727,\"sequence\":18446744073709551615,\"instant\":\"2026-08-24T12:34:56.1234567Z\"}";
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path, Collections = [collection] });
            OperationResult<RecordEnvelope> noncanonical = await store.CreateAsync(collection, new RecordCreateRequest
            {
                RequestedId = RecordId.Create("noncanonical"),
                Payload = Payload("{\"amount\":1.0,\"sequence\":1,\"instant\":\"2026-08-24T12:34:56.1234567Z\"}")
            }, Operation(BaseOperationKind.Create));
            noncanonical.Status.Should().Be(OperationStatus.ValidationFailed, noncanonical.Error?.Code + ": " + noncanonical.Error?.Message);
            noncanonical.Error!.Code.Should().Be(BaseSchemaErrorCodes.ScalarConstraintViolated);
            OperationResult<RecordEnvelope> created = await store.CreateAsync(collection, new RecordCreateRequest { RequestedId = RecordId.Create("exact"), Payload = Payload(json) }, Operation(BaseOperationKind.Create));
            created.IsSuccess().Should().BeTrue(created.Error?.Code + ": " + created.Error?.Message);
            OperationResult<RecordEnvelope> read = await store.GetAsync(collection, RecordId.Create("exact"), Operation(BaseOperationKind.Get));
            Dictionary<string, JsonElement> values = SqliteRecordSerializer.NormalizeObjectPayload(read.Value!.Payload).Fields!;
            values["amount"].GetRawText().Should().Be("170141183460469231731687303715884105727");
            values["sequence"].GetRawText().Should().Be("18446744073709551615");
            values["instant"].GetString().Should().Be("2026-08-24T12:34:56.1234567Z");
        }
        finally { foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate); }
    }

    [Fact]
    public async Task DecimalOrderingShadowMatchesTheExactLogicalComparatorAcrossMissingNullAndInt128Range()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-decimal-order-" + Guid.NewGuid().ToString("N") + ".db");
        CollectionDefinition collection = Collection() with
        {
            Fields =
            [
                ScalarField("item.amount", "amount", "number", "decimal", BaseScalarKind.Decimal, BaseFieldPresence.Optional, BaseFieldNullability.Nullable),
                StringField("item.tenant", "tenant", BaseFieldPresence.Required, BaseFieldNullability.NonNullable),
            ],
            Indexes = [new BaseLogicalIndexDefinition
            {
                Id = BaseLogicalIndexId.Create("item.amount-order"), Version = 1, CollectionId = "items", Unique = false, StoreRequired = true,
                Parts = [new BaseLogicalIndexPart { FieldOrdinal = 0, Direction = BaseIndexSortDirection.Ascending, Collation = BaseIndexCollation.OrdinalBinary, NullOrder = BaseIndexNullOrder.MissingThenNullThenValue }],
                MembershipPredicate = TruePredicate(), Checksum = default,
            }],
        };
        (string Id, string Json)[] rows =
        [
            ("max", "{\"tenant\":\"t\",\"amount\":170141183460469231731687303715884105727}"),
            ("one", "{\"tenant\":\"t\",\"amount\":1}"), ("tenth", "{\"tenant\":\"t\",\"amount\":0.1}"),
            ("zero", "{\"tenant\":\"t\",\"amount\":0}"), ("negative-tenth", "{\"tenant\":\"t\",\"amount\":-0.1}"),
            ("negative-one", "{\"tenant\":\"t\",\"amount\":-1}"),
            ("min", "{\"tenant\":\"t\",\"amount\":-170141183460469231731687303715884105728}"),
            ("same-b", "{\"tenant\":\"t\",\"amount\":1}"), ("same-a", "{\"tenant\":\"t\",\"amount\":1}"),
            ("null", "{\"tenant\":\"t\",\"amount\":null}"), ("missing", "{\"tenant\":\"t\"}"),
        ];
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path, Collections = [collection] });
            foreach ((string id, string json) in rows)
            {
                OperationResult<RecordEnvelope> created = await store.CreateAsync(collection, new RecordCreateRequest { RequestedId = RecordId.Create(id), Payload = Payload(json) }, Operation(BaseOperationKind.Create));
                created.IsSuccess().Should().BeTrue(id + ":" + created.Error?.Code + ":" + created.Error?.Message);
            }
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()); await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT record_id FROM {PhysicalTable("items")} ORDER BY {PhysicalRank("item.amount-order", 0)}, {PhysicalOrder("item.amount-order", 0)} COLLATE BINARY, record_id;";
            var actual = new List<string>(); await using SqliteDataReader reader = await command.ExecuteReaderAsync(); while (await reader.ReadAsync()) actual.Add(reader.GetString(0));
            string[] expected = rows.Select(row => (Id: RecordId.Create(row.Id), Payload: SqliteRecordSerializer.NormalizeObjectPayload(Payload(row.Json))))
                .OrderBy(static value => value, Comparer<(RecordId Id, RecordPayload Payload)>.Create((left, right) => BaseLogicalIndexEvaluator.Compare(collection, collection.Indexes[0], left.Payload, left.Id, right.Payload, right.Id)))
                .Select(static value => value.Id.Value).ToArray();
            actual.Should().Equal(expected);
        }
        finally { foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate); }
    }

    private static CollectionDefinition Collection() => new() { Id = "items", Name = "items", Kind = BaseCollectionKinds.Document, SchemaMode = SchemaMode.Loose, UnknownFields = UnknownFieldPolicy.Preserve };
    private static string PhysicalTable(string collectionId) => "b_c_" + Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(collectionId)))[..32];
    private static string PhysicalField(string fieldId) => "f_" + Digest(fieldId);
    private static string PhysicalPresence(string fieldId) => "p_" + Digest(fieldId);
    private static string PhysicalRank(string indexId, int ordinal) => "r_" + Digest(indexId + ":" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static string PhysicalOrder(string indexId, int ordinal) => "o_" + Digest(indexId + ":" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static string PhysicalIndex(string indexId) => "b_i_" + Digest(indexId);
    private static string Digest(string id) => Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))[..32];
    private static FieldDefinition StringField(string id, string wireName, BaseFieldPresence presence, BaseFieldNullability nullability)
    {
        BaseScalarCodecAuthority codec = BaseGeneratedSchemaRegistration.ScalarCodec(BaseScalarKind.String);
        var constraints = new BaseScalarConstraintSet();
        return new FieldDefinition
        {
            Id = id, ApplicationName = wireName, WireName = wireName, Type = BaseFieldTypes.String,
            Presence = presence, Nullability = nullability, ScalarKind = BaseScalarKind.String,
            ScalarCodec = codec,
            ScalarConstraints = constraints,
            ScalarConstraintChecksum = BaseGeneratedSchemaRegistration.ScalarConstraintChecksum("items", id, presence, nullability, codec, constraints),
        };
    }
    private static FieldDefinition ScalarField(string id, string wireName, string type, string? format, BaseScalarKind kind, BaseFieldPresence presence = BaseFieldPresence.Required, BaseFieldNullability nullability = BaseFieldNullability.NonNullable)
    {
        BaseScalarCodecAuthority codec = BaseGeneratedSchemaRegistration.ScalarCodec(kind); var constraints = new BaseScalarConstraintSet();
        return new FieldDefinition
        {
            Id = id, ApplicationName = wireName, WireName = wireName, Type = type, Format = format,
            Presence = presence, Nullability = nullability, ScalarKind = kind,
            ScalarCodec = codec, ScalarConstraints = constraints,
            ScalarConstraintChecksum = BaseGeneratedSchemaRegistration.ScalarConstraintChecksum("items", id, presence, nullability, codec, constraints),
        };
    }
    private static FieldDefinition BinaryField(string id, string wireName, int maximum)
    {
        BaseScalarCodecAuthority codec = BaseGeneratedSchemaRegistration.ScalarCodec(BaseScalarKind.Binary); var constraints = new BaseScalarConstraintSet { MaximumBinaryBytes = maximum };
        return new FieldDefinition
        {
            Id = id, ApplicationName = wireName, WireName = wireName, Type = "string", Format = "base64", MaximumBytes = maximum,
            Presence = BaseFieldPresence.Required, Nullability = BaseFieldNullability.NonNullable, ScalarKind = BaseScalarKind.Binary,
            ScalarCodec = codec, ScalarConstraints = constraints,
            ScalarConstraintChecksum = BaseGeneratedSchemaRegistration.ScalarConstraintChecksum("items", id, BaseFieldPresence.Required, BaseFieldNullability.NonNullable, codec, constraints),
        };
    }
    private static BaseIndexPredicateRegistry TruePredicate() => new() { Root = BaseIndexPredicateId.Create("root"), Nodes = [new BaseIndexPredicateNode { Id = BaseIndexPredicateId.Create("root"), Kind = BaseIndexPredicateNodeKind.True }], Checksum = default };
    private static OperationContext Operation(BaseOperationKind kind) => new() { Operation = kind, CollectionId = "items", Now = DateTimeOffset.UnixEpoch };
    private static RecordPayload Payload(string json = "{\"title\":\"schema\"}")
    {
        using var document = JsonDocument.Parse(json);
        return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
    }
}
