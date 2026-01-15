# Directory Structure

```
proto/
  google/
    api/
      annotations.proto (31 lines)
      http.proto (370 lines)
    protobuf/
      empty.proto (51 lines)
    rpc/
      code.proto (186 lines)
  kurrent/
    rpc/
      errors.proto (94 lines)
      rpc.proto (83 lines)
  kurrentdb/
    protocol/
      v0/
        .scratch/
          processors_draft.proto (876 lines)
          streams_manage.proto (122 lines)
          streams_read.proto (148 lines)
          streams.proto (542 lines)
        admin/
          features.proto (129 lines)
          service.proto (19 lines)
        connectors/
          connectors.proto (226 lines)
          errors.proto (37 lines)
        persistent_susbcriptions/
          errors.proto (40 lines)
        projections/
          errors.proto (45 lines)
          projections.proto (99 lines)
      v1/
        code.proto (186 lines)
        gossip.proto (46 lines)
        operations.proto (47 lines)
        persistentsubscriptions.proto (368 lines)
        projections.proto (177 lines)
        serverfeatures.proto (22 lines)
        shared.proto (58 lines)
        status.proto (50 lines)
        streams.proto (351 lines)
        users.proto (100 lines)
      v2/
        indexes/
          indexes.proto (135 lines)
        processors/
          checkpointing.proto (21 lines)
          events.proto (283 lines)
          processors.proto (536 lines)
          shared.proto (200 lines)
          workers.proto (25 lines)
        registry/
          errors.proto (30 lines)
          groups.proto (126 lines)
          schemas.proto (191 lines)
          service.proto (61 lines)
          shared.proto (61 lines)
          validation.proto (76 lines)
        streams/
          errors.proto (224 lines)
          shared.proto (132 lines)
          streams.proto (396 lines)
src/
  Kurrent.Client.Schema.Protobuf/
    Kurrent.Client.Schema.Protobuf.csproj (14 lines)
    ProtobufExtensions.cs (106 lines)
    ProtobufSchemaSerializer.cs (16 lines)
  Kurrent.Client.SourceGenerators/
    Kurrent.Client.SourceGenerators.csproj (23 lines)
    KurrentOperationErrorGenerator.cs (371 lines)
    ResultBaseImplicitOperatorsGenerator.cs (176 lines)
    SourceGeneratorFileNaming.cs (133 lines)
    ValueObjectGenerator.cs (225 lines)
  Kurrent.Grpc.LoadBalancing/
    Builder/
      GrpcLoadBalancedChannel.cs (52 lines)
      LoadBalancingBuilder.cs (141 lines)
      LoadBalancingServiceBuilder.cs (149 lines)
    Configuration/
      LoadBalancingOptions.cs (34 lines)
      RefreshPolicy.cs (64 lines)
      ResilienceOptions.cs (36 lines)
    Exceptions/
      DiscoveryException.cs (40 lines)
    Extensions/
      ServiceCollectionExtensions.cs (40 lines)
    Infrastructure/
      ClusterLoadBalancer.cs (23 lines)
      ClusterLoadBalancerFactory.cs (19 lines)
      ClusterNodeAttributes.cs (22 lines)
      ClusterPicker.cs (61 lines)
      ClusterResolver.cs (230 lines)
      ClusterResolverFactory.cs (29 lines)
      RefreshOnErrorInterceptor.cs (95 lines)
    Model/
      ClusterNode.cs (48 lines)
      ClusterTopology.cs (42 lines)
      NodeRole.cs (19 lines)
      NodeStatus.cs (19 lines)
      TopologyContext.cs (20 lines)
    Picking/
      DefaultStrategy.cs (98 lines)
      IClusterNodePickingStrategy.cs (12 lines)
      LeaderOnlyStrategy.cs (22 lines)
      PickNodeContext.cs (7 lines)
      PickNodeResult.cs (62 lines)
      RoundRobinStrategy.cs (36 lines)
      SubchannelNode.cs (8 lines)
    Sources/
      PollingTopologySource.cs (65 lines)
      TopologySource.cs (39 lines)
    asdsad.cs (1883 lines)
    Kurrent.Grpc.LoadBalancing.csproj (16 lines)
  KurrentDB.Client/
    Kurrent/
      DuckDb/
        DuckLake/
          DuckDBConnection.DuckLake.cs (30 lines)
          DuckLakeConfigurator.cs (199 lines)
          DuckLakeEnums.cs (55 lines)
          DuckLakeOptions.cs (338 lines)
          DuckLakeOptionsBuilder.cs (508 lines)
        MotherDuck/
          DuckDBConnection.MotherDuck.cs (15 lines)
          MotherDuckOptions.cs (22 lines)
        DataSourceType.cs (22 lines)
        DuckDBConnection.cs (35 lines)
        DuckDBConnectionBuilder.cs (178 lines)
        DuckDBConnectionOptions.cs (81 lines)
        DuckDBConnectionOptionsParser.cs (198 lines)
        DuckDBConnectionStringTests.cs (620 lines)
        DuckDBLoggingOptions.cs (24 lines)
        DuckDBNativeConfiguration.cs (92 lines)
      GoogleRpc/
        DebugInfoExtensions.cs (30 lines)
        Exceptions.cs (35 lines)
        StatusExtensions.cs (6 lines)
      Grpc/
        Compression/
          BrotliCompressionProvider.cs (16 lines)
          CompressionInterceptor.cs (59 lines)
          GrpcCompression.cs (132 lines)
        Interceptors/
          HeadersInterceptor.cs (73 lines)
        AsyncStreamReaderExtensions.cs (19 lines)
        GrpcCredentialsHelper.cs (76 lines)
        NonCapturingTimer.cs (38 lines)
      Http/
        EnsureSuccessStatusHandler.cs (8 lines)
        HttpSecurity.cs (81 lines)
      Options/
        FluentOptionsValidator.cs (26 lines)
        OptionsBase.cs (22 lines)
        OptionsBuilder.cs (63 lines)
      Talyn/
        TalynBus.cs (1178 lines)
        TalynDependencyInjection.cs (333 lines)
        TalynOutcomes.cs (146 lines)
        TalynReceivers.cs (349 lines)
        TalynRegistry.cs (380 lines)
      Validation/
        Validation.cs (114 lines)
      TestValueObject.cs (34 lines)
      ValueObject.cs (169 lines)
      ValueObjectAttribute.cs (6 lines)
    Kurrent.Client/
      Errors/
        ErrorCodeExtensions.cs (12 lines)
        Exceptions.cs (111 lines)
        KurrentErrorDetails.cs (98 lines)
        KurrentException.cs (76 lines)
        KurrentOperationErrorAttribute.cs (9 lines)
        VariantResultErrorExtensions.cs (17 lines)
      Internal/
        Diagnostics/
          Telemetry/
            TelemetryTags.cs (29 lines)
          Tracing/
            TracingConstants.cs (8 lines)
            TracingMetadata.cs (27 lines)
          ActivityExtensions.cs (38 lines)
          ActivityStatus.cs (9 lines)
          ActivityStatusCodeHelper.cs (18 lines)
          ActivityTagsCollectionExtensions.cs (21 lines)
          ExceptionExtensions.cs (21 lines)
        Extensions/
          ArrayExtensions.cs (22 lines)
          AsyncEnumerableExtensions.cs (185 lines)
          ChannelExtensions.cs (71 lines)
          EnumerableTaskExtensions.cs (20 lines)
          EnumExtensions.cs (23 lines)
          GuidExtensions.cs (8 lines)
          ObjectExtensions.cs (9 lines)
          Utf8JsonReaderExtensions.cs (13 lines)
        AppVersionInfo.cs (82 lines)
        AssemblyLoader.cs (177 lines)
        AssemblyScanner.cs (241 lines)
        CertificateLoader.cs (55 lines)
        ConcurrentBidirectionalDictionary.cs (359 lines)
        ManualResetEventSlimExtensions.cs (57 lines)
        RpcMetadataExtensions.cs (12 lines)
        SystemTypes.cs (73 lines)
        ThrowableException.cs (13 lines)
        UriDataStringExtensions.cs (22 lines)
        WithExtension.cs (22 lines)
      Modules/
        Admin/
          Model/
            Features/
              EnforcementLevel.cs (18 lines)
              Feature.cs (74 lines)
              FeatureRequirement.cs (82 lines)
              ServerFeatures.cs (78 lines)
              ServerInfo.cs (22 lines)
            ErrorResults.cs (22 lines)
            ScavengeResult.cs (46 lines)
            ScavengeStatus.cs (22 lines)
            SubsystemState.cs (12 lines)
          AdminClient.cs (88 lines)
          AdminClient.Features.cs (21 lines)
          AdminClient.Scavenge.cs (61 lines)
          ErrorDetails.cs (9 lines)
        Connectors/
          Model/
            ConnectorDetails.cs (71 lines)
            ConnectorListOptions.cs (34 lines)
            ConnectorListResult.cs (18 lines)
            ConnectorSettingsResult.cs (14 lines)
            ConnectorState.cs (26 lines)
            ConnectorType.cs (6 lines)
            ErrorResults.cs (38 lines)
            NodeAffinity.cs (16 lines)
          ConnectorsClient.cs (405 lines)
          ErrorDetails.cs (19 lines)
        PersistentSubscriptions/
          Model/
            Exceptions/
              MaximumSubscribersReachedException.cs (10 lines)
              PersistentSubscriptionDroppedByServerException.cs (10 lines)
              PersistentSubscriptionNotFoundException.cs (33 lines)
            ErrorResults.cs (40 lines)
            ListPersistentSubscriptionsOptions.cs (6 lines)
            PersistentSubscription.cs (183 lines)
            PersistentSubscriptionConnectionInfo.cs (42 lines)
            PersistentSubscriptionDetails.cs (9 lines)
            PersistentSubscriptionDetails.Mapping.cs (78 lines)
            PersistentSubscriptionMessage.cs (32 lines)
            PersistentSubscriptionNakAction.cs (26 lines)
            PersistentSubscriptionSettings.cs (62 lines)
            PersistentSubscriptionStats.cs (51 lines)
            PersistentSubscriptionType.cs (19 lines)
            PersistentSubscriptionV1Mapper.cs (232 lines)
            StreamIdentifier.cs (18 lines)
            SubscriptionDroppedReason.cs (18 lines)
            SubscriptionGroupName.cs (15 lines)
            TimeSpanRangeAttribute.cs (34 lines)
          ErrorDetails.cs (37 lines)
          PersistentSubscriptionsClient.cs (205 lines)
          PersistentSubscriptionsClient.Extensions.cs (6 lines)
          PersistentSubscriptionsClient.Subscribe.cs (395 lines)
        Processors/
          Checkpointing/
            CheckpointState.cs (36 lines)
            IPositionTracker.cs (106 lines)
            IWorkerCheckpointManager.cs (165 lines)
            PositionTracker.cs (134 lines)
            WorkerCheckpointManager.cs (150 lines)
            WorkerCheckpointStore.cs (156 lines)
          Exceptions/
            ProcessExceptions.cs (7 lines)
            RecordProcessorExceptions.cs (48 lines)
          Gateway/
            Messages/
              Enums.cs (136 lines)
              GatewayMessages.cs (44 lines)
              WorkerMessages.cs (42 lines)
            IProcessorGateway.cs (35 lines)
            IWorkerSession.cs (36 lines)
            LocalGateway.cs (204 lines)
            LocalRelaySession.cs (54 lines)
          Models/
            StreamTemplate.cs (27 lines)
            SubscriptionInitialPosition.cs (46 lines)
            WorkerSessionInfo.cs (2 lines)
          Partitioning/
            HashGenerators.cs (26 lines)
            IPartitionAssigner.cs (140 lines)
            PartitionAssigner.cs (137 lines)
            PartitionCalculator.cs (91 lines)
          Server/
            BlobKey.cs (84 lines)
            BlobMetadata.cs (82 lines)
            IBlobStore.cs (88 lines)
            IProcessorCheckpointManager.cs (141 lines)
            ProcessorCheckpointManager.cs (148 lines)
          State/
            DuckDb/
              DuckDbStateStore.cs (412 lines)
              DuckDbValueStateBuilder.cs (230 lines)
            MapState/
              IMapEntryBuilder.cs (151 lines)
              IMapStateBuilder.cs (126 lines)
            Results/
              UpsertResult.cs (41 lines)
              WriteResult.cs (66 lines)
            TableState/
              ITableStateBuilder.cs (324 lines)
            ValueState/
              IValueStateBuilder.cs (250 lines)
            ICheckpointStateStore.cs (124 lines)
            IStateStore.cs (163 lines)
            PartitionRange.cs (55 lines)
            StateKey.cs (58 lines)
          IRecordProcessor.cs (282 lines)
          IRecordProcessorFactory.cs (27 lines)
          KurrentProcessorBuilder.cs (434 lines)
          KurrentProcessors.cs (566 lines)
          ProcessorSettings.cs (323 lines)
        Projections/
          Model/
            DeleteProjectionOptions.cs (8 lines)
            ErrorResults.cs (57 lines)
            GetProjectionOptions.cs (7 lines)
            ListProjectionsOptions.cs (27 lines)
            ProjectionCheckpointStatus.cs (6 lines)
            ProjectionDefinition.cs (25 lines)
            ProjectionDetails.cs (22 lines)
            ProjectionMode.cs (7 lines)
            ProjectionName.cs (16 lines)
            ProjectionPartition.cs (15 lines)
            ProjectionSettings.cs (111 lines)
            ProjectionsMapper.cs (165 lines)
            ProjectionStatistics.cs (56 lines)
            ProjectionStatus.cs (19 lines)
            ProjectionType.cs (13 lines)
          ErrorDetails.cs (21 lines)
          ProjectionsClient.cs (377 lines)
          ProjectionsClient.Extensions.cs (17 lines)
          ProjectionsClient.IdiomaticExtensions.cs (45 lines)
        Registry/
          Models/
            CompatibilityMode.cs (35 lines)
            ErrorResults.cs (33 lines)
            FieldViolation.cs (11 lines)
            Schema.cs (28 lines)
            SchemaCompatibilityError.cs (46 lines)
            SchemaDetails.cs (30 lines)
            SchemaIdentifier.cs (118 lines)
            SchemaName.cs (13 lines)
            SchemaVersion.cs (20 lines)
            SchemaVersionDescriptor.cs (4 lines)
            SchemaVersionId.cs (20 lines)
          Exceptions.cs (7 lines)
          RegistryClient.cs (252 lines)
          RegistryClient.Extensions.cs (36 lines)
        Streams/
          Metadata/
            DefaultMetadataDecoder.cs (77 lines)
            Metadata.cs (677 lines)
            MetadataDecoder.cs (89 lines)
            MetadataSchemaExtensions.cs (40 lines)
          Models/
            Filters/
              EmptyFilter.cs (10 lines)
              ExpressionFilter.cs (40 lines)
              ExpressionFilter.Parser.cs (687 lines)
              ExpressionFilter.ServerExtractor.cs (163 lines)
              RecordFilter.cs (87 lines)
              RegexFilter.cs (52 lines)
              SchemaFilter.cs (23 lines)
              StreamFilter.cs (58 lines)
            Maps/
              ProtocolMapper.cs (75 lines)
              StreamsClientV1Mapper.cs (272 lines)
              StreamsClientV2Mapper.cs (79 lines)
            Scratch/
              Heartbeat.cs (80 lines)
              HeartbeatOptions.cs (11 lines)
              IndexName.cs (35 lines)
              Partition.cs (36 lines)
            AppendRecord.cs (41 lines)
            Errors.cs (0 lines)
            ExpectedStreamState.cs (31 lines)
            LogPosition.cs (99 lines)
            ReadDirection.cs (14 lines)
            ReadOptions.cs (131 lines)
            Record.cs (172 lines)
            RecordBatchStream.cs (158 lines)
            RecordPosition.cs (133 lines)
            RecordSchemaInfo.cs (24 lines)
            RecordStream.cs (68 lines)
            RedactionExtensions.cs (20 lines)
            SchemaDataFormat.cs (70 lines)
            SequenceId.cs (97 lines)
            StartPosition.cs (8 lines)
            StreamCategory.cs (161 lines)
            StreamDataRetentionOptions.cs (8 lines)
            StreamInfo.cs (37 lines)
            StreamMetadata.cs (279 lines)
            StreamName.cs (215 lines)
            StreamRevision.cs (63 lines)
          Operations/
            StreamsClient.Append.cs (35 lines)
            StreamsClient.Append.Extensions.cs (18 lines)
            StreamsClient.Append.IdiomaticExtensions.cs (24 lines)
            StreamsClient.Append.Mappers.cs (62 lines)
            StreamsClient.Append.Models.Builders.cs (104 lines)
            StreamsClient.Append.Models.cs (50 lines)
            StreamsClient.Manage.cs (186 lines)
            StreamsClient.Manage.Errors.cs (82 lines)
            StreamsClient.Manage.Extensions.cs (33 lines)
            StreamsClient.Process.cs (913 lines)
            StreamsClient.Process.YetAnotherSetOfCrap.cs (497 lines)
            StreamsClient.Read.cs (288 lines)
            StreamsClient.Read.Errors.cs (74 lines)
            StreamsClient.Read.Extensions.cs (35 lines)
            StreamsClient.Read.IdiomaticExtensions.cs (5 lines)
          Exceptions.cs (72 lines)
          StreamsClient.cs (13 lines)
        Users/
          Model/
            StreamPolicy/
              AuthorizationType.cs (23 lines)
              DefaultPolicyRules.cs (38 lines)
              StreamAccessPolicy.cs (124 lines)
              StreamPolicyConfiguration.cs (111 lines)
              StreamPolicyConfigurationBuilder.cs (143 lines)
              StreamPrefixRule.cs (33 lines)
            ErrorResults.cs (49 lines)
            LoginName.cs (15 lines)
            UserDetails.cs (38 lines)
          UsersClient.cs (408 lines)
        ClientModuleBase.cs (29 lines)
      Options/
        Connection/
          ConnectionOptionsBuilder.cs (167 lines)
          KurrentClientGossipOptions.cs (91 lines)
          KurrentClientSecurityOptions.ClientCredentials.cs (134 lines)
          KurrentClientSecurityOptions.cs (105 lines)
          KurrentClientSecurityOptions.TransportSecurity.cs (219 lines)
        Validators/
          KurrentClientGossipOptionsValidator.cs (33 lines)
          KurrentClientOptionsValidator.cs (57 lines)
          KurrentClientResilienceOptionsValidator.cs (53 lines)
          KurrentClientSchemaOptionsValidator.cs (2 lines)
          KurrentClientSecurityOptionsValidator.cs (31 lines)
        KurrentClientOptions.cs (301 lines)
        KurrentClientOptionsBuilder.cs (216 lines)
        KurrentClientOptionsExtensions.cs (103 lines)
        KurrentClientResilienceOptions.cs (388 lines)
        KurrentClientSchemaOptions.cs (177 lines)
      Schema/
        NameStrategies/
          CategorySchemaNameStrategy.cs (18 lines)
          MessageSchemaNameStrategy.cs (9 lines)
          NamespaceCategorySchemaNameStrategy.cs (22 lines)
          NamespaceSchemaNameStrategy.cs (22 lines)
          SchemaNameExtensions.cs (8 lines)
          SchemaNameStrategies.cs (19 lines)
          SchemaNameStrategyBase.cs (51 lines)
          SchemaUrn.cs (122 lines)
        Serialization/
          Bytes/
            BytesPassthroughSerializer.cs (34 lines)
          Json/
            JsonSchemaSerializer.cs (14 lines)
            JsonSchemaSerializerOptions.cs (23 lines)
            JsonSerializer.cs (62 lines)
            NullableTimeSpanIso8601Converter.cs (36 lines)
          Protobuf/
            ProtobufExtensions.cs (102 lines)
            ProtobufSchemaSerializer.cs (16 lines)
          ISchemaSerializer.cs (25 lines)
          SchemaManager.cs (242 lines)
          SchemaSerializationContext.cs (23 lines)
          SchemaSerializationExceptions.cs (43 lines)
          SchemaSerializerBase.cs (97 lines)
          SchemaSerializerProvider.cs (39 lines)
        MessageMappingBuilder.cs (59 lines)
        MessageTypeMapper.cs (182 lines)
        MessageTypeMapper.Errors.cs (72 lines)
        MessageTypeMapper.Exceptions.cs (52 lines)
        MessageTypeMapper.Extensions.cs (67 lines)
        NJsonSchemaExporter.cs (20 lines)
        ResolvedSchemaRegistryPolicy.cs (98 lines)
        SchemaRegistrationOptions.cs (85 lines)
        SchemaRegistryPolicy.cs (126 lines)
      KurrentBackdoorClientFactory.cs (150 lines)
      KurrentClient.cs (65 lines)
      KurrentConnectionString.cs (151 lines)
      SystemConstants.cs (26 lines)
      SystemMetadataKeys.cs (21 lines)
    Legacy/
      RipAndTear/
        Client/
          Exceptions/
            DiscoveryException.cs (26 lines)
            RequiredMetadataPropertyMissingException.cs (15 lines)
          Gossip/
            Selectors/
              ChannelSelector.cs (12 lines)
              GossipChannelSelector.cs (66 lines)
              IChannelSelector.cs (9 lines)
              SingleNodeChannelSelector.cs (21 lines)
            ChannelCache.cs (99 lines)
            ChannelFactoryExtensions.cs (68 lines)
            ChannelInfo.cs (4 lines)
            ClusterMessage.cs (24 lines)
            EndPointExtensions.cs (48 lines)
            GrpcGossipClient.cs (23 lines)
            GrpcServerCapabilitiesClient.cs (58 lines)
            IGossipClient.cs (5 lines)
            IServerCapabilitiesClient.cs (5 lines)
            NodePreference.cs (22 lines)
            NodePreferenceComparers.cs (39 lines)
            NodeSelector.cs (44 lines)
            ReconnectionRequired.cs (11 lines)
            ServerCapabilities.cs (12 lines)
            SharingProvider.cs (94 lines)
            SingleNodeHttpHandler.cs (9 lines)
          Interceptors/
            AuthenticationInterceptor.cs (64 lines)
            LeaderNotFoundInterceptor.cs (107 lines)
            TypedExceptionInterceptor.cs (132 lines)
          Settings/
            KurrentDBClientConnectivitySettings.cs (135 lines)
            KurrentDBClientOperationOptions.cs (35 lines)
            KurrentDBClientRetrySettings.cs (59 lines)
            KurrentDBClientSettings.cs (218 lines)
          Constants.cs (81 lines)
          KurrentDBCallOptions.cs (61 lines)
          LegacyClusterClient.cs (119 lines)
        EpochExtensions.cs (6 lines)
        HashCode.cs (68 lines)
        StreamIdentifier.cs (20 lines)
        TaskExtensions.cs (16 lines)
        UserCredentials.cs (50 lines)
        Uuid.cs (144 lines)
        X509Certificates.cs (50 lines)
      KurrentDBLegacyCallInvoker.cs (157 lines)
      KurrentDBLegacyErrorCodes.cs (20 lines)
      KurrentDBLegacyExceptionExtensions.cs (24 lines)
      KurrentDBLegacySettingsConverter.cs (83 lines)
      SystemStreams.cs (46 lines)
    KurrentDB.Client.csproj (230 lines)
  Variant/
    Result/
      Result.Abstractions.cs (34 lines)
      Result.cs (88 lines)
      Result.Errors.cs (205 lines)
      Result.Functional.Bind.cs (54 lines)
      Result.Functional.Inspect.cs (111 lines)
      Result.Functional.Map.cs (111 lines)
      Result.Functional.Match.cs (123 lines)
      Result.Functional.Switch.cs (139 lines)
      Result.Functional.Unwrap.cs (71 lines)
      Result.Try.cs (339 lines)
      Results.cs (19 lines)
    IVariant.cs (40 lines)
    IVariantException.cs (451 lines)
    Variant.csproj (11 lines)
  Variant.SourceGenerators/
    SourceGeneratorFileNaming.cs (133 lines)
    Variant.SourceGenerators.csproj (21 lines)
    VariantGenerator.cs (612 lines)
    VariantResultsGenerator.cs (939 lines)
test/
  Kurrent.Client.Benchmarks/
    Benchmarks/
      ReadBatchesBenchmarks.cs (331 lines)
      ReadLogBenchmarks.cs (57 lines)
      ReadStreamBenchmarks.cs (96 lines)
    Infrastructure/
      BenchmarkFixture.cs (48 lines)
    BenchmarkConfig.cs (23 lines)
    Kurrent.Client.Benchmarks.csproj (19 lines)
    Program.cs (4 lines)
  Kurrent.Client.Integration.Tests/
    Admin/
      AdminTests.cs (36 lines)
    Projections/
      ProjectionsStateTests.cs (35 lines)
      ProjectionsTests.cs (450 lines)
    Registry/
      CheckSchemaCompatibilityTests.cs (66 lines)
      CreateSchemaTests.cs (38 lines)
      DeleteSchemaTests.cs (25 lines)
      GetSchemaTests.cs (55 lines)
      GetSchemaVersionTests.cs (91 lines)
      KurrentClientTestFixture.Helpers.cs (76 lines)
    Streams/
      Append/
        AppendLoadTests.cs (41 lines)
        AppendTests.cs (114 lines)
      CatchUpSubscriptions/
        SusbcriptionTests.cs (87 lines)
      Management/
        DeleteTests.cs (68 lines)
        StreamInfoTests.cs (154 lines)
        TombstoneTests.cs (66 lines)
        TruncateTests.cs (46 lines)
      Read/
        InspectRecordTests.cs (121 lines)
        ReadTests.cs (221 lines)
        RecordProcessorTests.cs (300 lines)
    Subscriptions/
      PersistentSubscriptionTests.cs (169 lines)
    Tools/
      CertificatesManager.cs (69 lines)
      HomeAutomationTestData.cs (62 lines)
    Users/
      KurrentClientTestFixture.Helpers.cs (71 lines)
      StreamPolicyTests.cs (214 lines)
      UsersTests.cs (113 lines)
    Kurrent.Client.Integration.Tests.csproj (58 lines)
    TestkitWireUp.cs (1 lines)
  Kurrent.Client.Tests/
    Filters/
      ExpressionFilter.DuckDbValidationTests.cs (50 lines)
      ExpressionFilterParserTests.cs (369 lines)
      ExpressionFilterTests.cs (451 lines)
      RecordFaker.cs (82 lines)
      RecordFilterTests.cs (49 lines)
      SchemaFilterTests.cs (28 lines)
      ServerFilterExtractorTests.cs (391 lines)
      StreamFilterTests.cs (92 lines)
    Internal/
      AsyncEnumerableExtensionsTests.cs (125 lines)
      ConcurrentBidirectionalDictionaryTests.cs (598 lines)
      EnumExtensionsTests.cs (37 lines)
      PromiseTests.cs (336 lines)
    Legacy/
      NodePreferenceComparerTests.cs (59 lines)
      NodeSelectorTests.cs (51 lines)
      SharingProviderTests.cs (219 lines)
      UuidTests.cs (49 lines)
    Model/
      MetadataDecoderTests.cs (48 lines)
      MetadataTests.cs (200 lines)
      StreamMetadataTests.cs (321 lines)
    Modules/
      Processors/
        Checkpoints/
          PositionTrackerTests.cs (249 lines)
        Partitioning/
          PartitionAssignerTests.cs (325 lines)
      Streams/
        ProcessorExceptionTests.cs (174 lines)
    Options/
      KurrentClientGossipOptionsTests.cs (52 lines)
      KurrentClientOptionsBuilderTests.cs (60 lines)
      KurrentClientOptionsTests.cs (486 lines)
    SchemaRegistry/
      NameStrategies/
        Sample.cs (7 lines)
        SchemaNameStrategiesTests.cs (79 lines)
        SchemaUrnTests.cs (193 lines)
    Talyn/
      TalynRegistryTests.cs (684 lines)
    ConnectionStringTests.cs (336 lines)
    Kurrent.Client.Tests.csproj (43 lines)
    TestkitWireUp.cs (1 lines)
    ValueObjectTests.cs (844 lines)
  Kurrent.Testing/
    Bogus/
      BogusFaker.cs (9 lines)
    Configuration/
      ConfigurationExtensions.cs (157 lines)
      NormalizedEnvironmentVariablesConfigurationSource.cs (140 lines)
    Containers/
      FluentDocker/
        FluentDockerBuilderExtensions.cs (62 lines)
        FluentDockerCompositeBuilderExtensions.cs (17 lines)
        FluentDockerContainerServiceExtensions.cs (31 lines)
        FluentDockerServiceExtensions.cs (46 lines)
        TestContainer.cs (120 lines)
        TestStack.cs (76 lines)
      KurrentDB/
        KurrentDBTestContainer.cs (174 lines)
    OpenTelemetry/
      OtelServiceMetadata.cs (58 lines)
      OtelTestContextExtensions.cs (15 lines)
    Sample/
      Games/
        TicTacToe/
          TicTacToeGame.cs (93 lines)
          TicTacToeGameTests.cs (332 lines)
          TicTacToeModel.cs (34 lines)
          TicTacToeSimulator.cs (33 lines)
          TicTacToeSimulatorTests.cs (181 lines)
        GamesAvailable.cs (4 lines)
      HomeAutomation/
        FakerExtensions.cs (17 lines)
        HomeAutomationDataSet.cs (193 lines)
        HomeAutomationEvents.cs (80 lines)
        HomeAutomationFakers.cs (400 lines)
        HomeAutomationModel.cs (189 lines)
    Shouldly/
      ShouldlyObjectGraphTestExtensions.cs (463 lines)
      ShouldlyObjectGraphTestExtensionsTests.cs (166 lines)
      ShouldlyToolkit.cs (155 lines)
    Testkit/
      ServiceCollectionExtensions.cs (13 lines)
      TestkitExecutionContext.cs (100 lines)
      TestkitLoggerProvider.cs (82 lines)
      TestkitTestConfiguratorAttribute.cs (16 lines)
      TestkitTestExecutor.cs (52 lines)
    TUnit/
      TestCaseGenerator.cs (31 lines)
      TestContextExtensions.cs (49 lines)
      TestContextLoggingExtensions.cs (23 lines)
    Disposable.cs (48 lines)
    Kurrent.Testing.csproj (60 lines)
  Variant.Tests/
    Result/
      ResultBindTests.cs (366 lines)
      ResultCoreTests.cs (146 lines)
      ResultInspectTests.cs (486 lines)
      ResultMapTests.cs (402 lines)
      ResultMatchTests.cs (334 lines)
      ResultStaticMethodsTests.cs (53 lines)
      ResultSwitchTests.cs (349 lines)
      ResultTestModels.cs (39 lines)
      ResultTryTests.cs (607 lines)
      ResultUnwrapTests.cs (155 lines)
    Variant.Tests.csproj (32 lines)
    VariantGenerationTests.cs (290 lines)
    VariantGeneratorTests.cs (34 lines)
Directory.Build.props (90 lines)
Directory.Packages.props (77 lines)
Kurrent.Client.slnx (44 lines)
```