using System.Text.Json;
using Npgsql;

namespace CimmeriaMcp.Services;

/// <summary>
/// Knowledge-graph queries over Postgres. Replaces the Cosmos-DB
/// `knowledge-graph` container with two relational tables —
/// `kg_vertices` and `kg_edges` — plus JSONB property bags that
/// preserve the snake_case field names the Cosmos documents had
/// (`from_id`, `to_id`, `method_type`, `data_type`, `replicated`,
/// `owner`, etc.). The JSON shapes returned to LLM callers are
/// unchanged.
///
/// Cosmos had two query patterns this port collapses into one:
///   - Vertices: `WHERE c.doc_type = 'vertex'` against the unified
///     container → now `FROM kg_vertices`.
///   - Edges:    `WHERE c.doc_type = 'edge'` against the same →
///     now `FROM kg_edges`.
/// The separate tables let the foreign-key cascade clean up dangling
/// edges automatically on vertex delete.
/// </summary>
public sealed class CimmeriaGraphService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly NpgsqlDataSource _db;

    public CimmeriaGraphService(NpgsqlDataSource db)
    {
        _db = db;
    }

    // ──────────────────────────────────────────────────────────────
    // Entity details
    // ──────────────────────────────────────────────────────────────

    public async Task<string> GetEntityDetailsAsync(string entityName)
    {
        var entityId = $"entity:{entityName}";
        var entity = await GetVertexAsync(entityId);
        if (entity is null)
        {
            return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found in knowledge graph." });
        }

        var props = await QueryVerticesByOwnerAndTypeAsync(entityName, "property");
        var methods = await QueryVerticesByOwnerAndTypeAsync(entityName, "method");
        var outgoing = await QueryEdgesByFromAsync(entityId);
        var incoming = await QueryEdgesByToAsync(entityId);

        return JsonSerializer.Serialize(new
        {
            entity,
            properties = props,
            methods,
            outgoing_edges = outgoing,
            incoming_edges = incoming,
        }, JsonOptions);
    }

    // ──────────────────────────────────────────────────────────────
    // Traversal
    // ──────────────────────────────────────────────────────────────

    public async Task<string> TraverseGraphAsync(string startEntity, string edgeType, int depth)
    {
        var startId = $"entity:{startEntity}";
        var visited = new HashSet<string> { startId };
        var results = new List<object>();
        var frontier = new Queue<(string id, int depth)>();
        frontier.Enqueue((startId, 0));

        // Edge types where "reverse direction also counts" — mirrors the
        // Cosmos query's special-case for inheritance edges.
        var bidirectional = edgeType is "inherits" or "implements" or "python_inherits";

        while (frontier.Count > 0)
        {
            var (currentId, currentDepth) = frontier.Dequeue();
            if (currentDepth >= depth) continue;

            var forward = await QueryEdgesByFromAndTypeAsync(currentId, edgeType);
            foreach (var edge in forward)
            {
                var toId = (string)edge["to_id"]!;
                results.Add(new
                {
                    from = currentId,
                    edge_type = edgeType,
                    to = toId,
                    depth = currentDepth + 1,
                });
                if (visited.Add(toId)) frontier.Enqueue((toId, currentDepth + 1));
            }

            if (bidirectional)
            {
                var reverse = await QueryEdgesByToAndTypeAsync(currentId, edgeType);
                foreach (var edge in reverse)
                {
                    var fromId = (string)edge["from_id"]!;
                    results.Add(new
                    {
                        from = fromId,
                        edge_type = edgeType,
                        to = currentId,
                        depth = currentDepth + 1,
                    });
                    if (visited.Add(fromId)) frontier.Enqueue((fromId, currentDepth + 1));
                }
            }
        }

        return JsonSerializer.Serialize(new
        {
            start = startEntity,
            edge_type = edgeType,
            max_depth = depth,
            traversal = results,
            nodes_visited = visited.Count,
        }, JsonOptions);
    }

    // ──────────────────────────────────────────────────────────────
    // Inheritance
    // ──────────────────────────────────────────────────────────────

    public async Task<string> GetInheritanceTreeAsync(string entityName)
    {
        var ancestors = new List<string>();
        var current = entityName;
        for (int i = 0; i < 10; i++)
        {
            var parents = await QueryEdgesByFromAndTypeAsync($"entity:{current}", "inherits");
            if (parents.Count == 0) break;
            var parent = ((string)parents[0]["to_id"]!).Replace("entity:", "");
            ancestors.Add(parent);
            current = parent;
        }

        var descEdges = await QueryEdgesByToAndTypeAsync($"entity:{entityName}", "inherits");
        var descendants = descEdges
            .Select(e => new { entity = ((string)e["from_id"]!).Replace("entity:", "") })
            .ToList();

        var ifaceEdges = await QueryEdgesByFromAndTypeAsync($"entity:{entityName}", "implements");
        var interfaces = ifaceEdges
            .Select(e => ((string)e["to_id"]!).Replace("entity:", ""))
            .ToList();

        return JsonSerializer.Serialize(new
        {
            entity = entityName,
            ancestors,
            descendants,
            interfaces,
        }, JsonOptions);
    }

    // ──────────────────────────────────────────────────────────────
    // Graph overview
    // ──────────────────────────────────────────────────────────────

    public async Task<string> GetGraphOverviewAsync()
    {
        // Vertex counts by label (label lives in properties JSONB).
        var vertexCounts = await ScalarGroupAsync(
            "SELECT properties->>'label' AS label, COUNT(*) AS count FROM kg_vertices GROUP BY properties->>'label'");
        var edgeCounts = await ScalarGroupAsync(
            "SELECT properties->>'label' AS label, COUNT(*) AS count FROM kg_edges GROUP BY properties->>'label'");

        var entities = await QueryVerticesByLabelAsync("entity", "name");
        var interfaces = await QueryVerticesByLabelAsync("interface", "name");
        var systems = await QueryVerticesByLabelAsync("game_system", "name", "description");

        return JsonSerializer.Serialize(new
        {
            vertex_counts = vertexCounts,
            edge_counts = edgeCounts,
            entities,
            interfaces,
            game_systems = systems,
        }, JsonOptions);
    }

    // ──────────────────────────────────────────────────────────────
    // Game system
    // ──────────────────────────────────────────────────────────────

    public async Task<string> GetGameSystemDetailsAsync(string systemName)
    {
        var sysId = $"system:{systemName}";
        var system = await GetVertexAsync(sysId);
        if (system is null)
        {
            return JsonSerializer.Serialize(new { error = $"Game system '{systemName}' not found." });
        }

        // Entities linked via `part_of_system` edge.
        var linkEdges = await QueryEdgesByToAndTypeAsync(sysId, "part_of_system");
        var entityDetails = new List<object>();
        foreach (var edge in linkEdges)
        {
            var entityId = (string)edge["from_id"]!;
            var entityName = entityId.Replace("entity:", "");

            var propCount = await CountVerticesByOwnerAndTypeAsync(entityName, "property");
            var methodCount = await CountVerticesByOwnerAndTypeAsync(entityName, "method");

            entityDetails.Add(new
            {
                entity = entityName,
                property_count = propCount,
                method_count = methodCount,
            });
        }

        return JsonSerializer.Serialize(new
        {
            system,
            entities = entityDetails,
        }, JsonOptions);
    }

    // ──────────────────────────────────────────────────────────────
    // Replicated properties
    // ──────────────────────────────────────────────────────────────

    public async Task<string> GetReplicatedPropertiesAsync(string entityName)
    {
        var chain = new List<string> { entityName };
        var current = entityName;
        for (int i = 0; i < 10; i++)
        {
            var parents = await QueryEdgesByFromAndTypeAsync($"entity:{current}", "inherits");
            if (parents.Count == 0) break;
            var parent = ((string)parents[0]["to_id"]!).Replace("entity:", "");
            chain.Add(parent);
            current = parent;
        }

        var ifaceEdges = await QueryEdgesByFromAndTypeAsync($"entity:{entityName}", "implements");
        foreach (var edge in ifaceEdges)
        {
            chain.Add(((string)edge["to_id"]!).Replace("entity:", ""));
        }

        var allProps = new List<object>();
        foreach (var owner in chain)
        {
            var sql = """
                SELECT properties FROM kg_vertices
                WHERE vertex_type = 'property'
                  AND properties->>'owner' = @owner
                  AND COALESCE((properties->>'replicated')::boolean, false) = true
                """;
            await using var cmd = _db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("owner", owner);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var props = JsonDocument.Parse(reader.GetString(0)).RootElement;
                allProps.Add(new
                {
                    name = props.GetProperty("name").GetString(),
                    data_type = TryGetString(props, "data_type"),
                    flags = TryGetString(props, "flags"),
                    defined_in = owner,
                });
            }
        }

        return JsonSerializer.Serialize(new
        {
            entity = entityName,
            inheritance_chain = chain,
            replicated_properties = allProps,
        }, JsonOptions);
    }

    // ──────────────────────────────────────────────────────────────
    // Method call chain
    // ──────────────────────────────────────────────────────────────

    public async Task<string> GetMethodCallChainAsync(string entityName, string methodName)
    {
        var callEdgeTypes = new[]
        {
            "sends_to_client", "sends_to_base", "sends_to_cell",
            "calls_self", "calls_super", "uses_bigworld"
        };

        var cellPrefix = $"script_method:cell:{entityName}.{methodName}";
        var basePrefix = $"script_method:base:{entityName}.{methodName}";

        var outgoing = await QueryEdgesByFromPrefixAndTypesAsync(cellPrefix, callEdgeTypes);
        var outgoingBase = await QueryEdgesByFromPrefixAndTypesAsync(basePrefix, callEdgeTypes);

        var incoming = await QueryEdgesByToMethodAndTypesAsync(methodName,
            new[] { "sends_to_client", "sends_to_base", "sends_to_cell", "calls_self" });

        return JsonSerializer.Serialize(new
        {
            entity = entityName,
            method = methodName,
            outgoing_calls = outgoing.Concat(outgoingBase).ToList(),
            incoming_calls = incoming,
        }, JsonOptions);
    }

    // ──────────────────────────────────────────────────────────────
    // Enum & type lookups
    // ──────────────────────────────────────────────────────────────

    public async Task<string> LookupEnumAsync(string enumName, string? tokenName)
    {
        if (!string.IsNullOrEmpty(tokenName))
        {
            // Token search across all enums. Tokens live as a JSON
            // object in `properties.tokens`; we expand them server-side
            // via jsonb_each_text so we can grep without dragging full
            // documents back to C#.
            var sql = """
                SELECT properties->>'name' AS enumeration,
                       tk.key AS token,
                       tk.value AS value
                FROM kg_vertices v,
                     LATERAL jsonb_each_text(COALESCE(v.properties->'tokens', '{}'::jsonb)) AS tk
                WHERE v.vertex_type = 'enumeration'
                  AND tk.key ILIKE @needle
                """;
            await using var cmd = _db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("needle", "%" + tokenName + "%");

            var matches = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                matches.Add(new
                {
                    enumeration = reader.GetString(0),
                    token = reader.GetString(1),
                    value = reader.IsDBNull(2) ? null : reader.GetString(2),
                });
            }
            return JsonSerializer.Serialize(new { query = tokenName, matches, count = matches.Count }, JsonOptions);
        }

        // Direct enum lookup by name.
        var enumProps = await QuerySingleVertexAsync("enumeration", enumName);
        if (enumProps is not null)
        {
            return JsonSerializer.Serialize(new { enumeration = enumProps }, JsonOptions);
        }

        // Fall back to constant search.
        var constants = await QueryVerticesContainingNameAsync("constant", enumName);
        return JsonSerializer.Serialize(new { query = enumName, constants, count = constants.Count }, JsonOptions);
    }

    public async Task<string> ResolveTypeAsync(string typeName)
    {
        var type = await QuerySingleVertexAsync("type_alias", typeName);
        if (type is null)
        {
            return JsonSerializer.Serialize(new { error = $"Type '{typeName}' not found." });
        }
        return JsonSerializer.Serialize(new { type_definition = type }, JsonOptions);
    }

    // ──────────────────────────────────────────────────────────────
    // Game data definitions
    // ──────────────────────────────────────────────────────────────

    public async Task<string> LookupGameDefAsync(string defName)
    {
        var def = await QuerySingleVertexAsync("game_def", defName);
        if (def is null)
        {
            var allDefs = await QueryVerticesByLabelAsync("game_def", "name", "field_count", "source_file");
            return JsonSerializer.Serialize(new
            {
                error = $"Game definition '{defName}' not found.",
                available_defs = allDefs,
            }, JsonOptions);
        }

        var refs = await QueryEdgesByFromPrefixAndTypesAsync($"game_def:{defName}", new[] { "references_def" });
        return JsonSerializer.Serialize(new
        {
            definition = def,
            cross_references = refs,
        }, JsonOptions);
    }

    // ──────────────────────────────────────────────────────────────
    // Implementation coverage
    // ──────────────────────────────────────────────────────────────

    public async Task<string> GetImplementationStatusAsync(string? entityName)
    {
        if (!string.IsNullOrEmpty(entityName))
        {
            var defMethods = await QueryVerticesAsync(
                """
                SELECT properties->>'name' AS name, properties->>'method_type' AS method_type
                FROM kg_vertices
                WHERE vertex_type = 'method' AND properties->>'owner' = @owner
                """,
                ("owner", entityName));

            var pyMethods = await QueryVerticesAsync(
                """
                SELECT properties->>'name' AS name FROM kg_vertices
                WHERE vertex_type = 'script_method' AND properties->>'owner_class' = @owner
                """,
                ("owner", entityName));

            // C++ implementations — by-name lookup across the whole graph.
            var cppMethods = await QueryVerticesAsync(
                """
                SELECT properties->>'name' AS name, properties->>'owner_class' AS owner_class, properties->>'source_file' AS source_file
                FROM kg_vertices
                WHERE vertex_type = 'cpp_method'
                """);

            var defNames = new HashSet<string>(defMethods.Select(m => (string)m["name"]!));
            var pyNames = new HashSet<string>(pyMethods.Select(m => (string)m["name"]!));
            var cppNames = new HashSet<string>(cppMethods.Select(m => (string)m["name"]!));

            var coverage = defMethods.Select(m =>
            {
                var name = (string)m["name"]!;
                return new
                {
                    method = name,
                    method_type = m["method_type"] as string,
                    has_python = pyNames.Contains(name),
                    has_cpp = cppNames.Contains(name),
                };
            }).ToList();

            return JsonSerializer.Serialize(new
            {
                entity = entityName,
                total_defined = defNames.Count,
                python_implemented = pyNames.Intersect(defNames).Count(),
                cpp_implemented = cppNames.Intersect(defNames).Count(),
                methods = coverage,
            }, JsonOptions);
        }

        var entities = await QueryVerticesAsync(
            """
            SELECT properties FROM kg_vertices WHERE vertex_type = 'entity'
            """);

        var cppByComponent = await ScalarGroupAsync(
            """
            SELECT properties->>'component' AS component, COUNT(*) AS count
            FROM kg_vertices WHERE vertex_type = 'cpp_class'
            GROUP BY properties->>'component'
            """);

        var cppTotal = await ScalarLongAsync(
            "SELECT COUNT(*) FROM kg_vertices WHERE vertex_type = 'cpp_method'");

        return JsonSerializer.Serialize(new
        {
            entities,
            cpp_classes_by_component = cppByComponent,
            total_cpp_methods = cppTotal,
            note = "Use entity_name parameter for per-entity method coverage",
        }, JsonOptions);
    }

    // ──────────────────────────────────────────────────────────────
    // Cross-reference
    // ──────────────────────────────────────────────────────────────

    public async Task<string> CrossReferenceAsync(string searchTerm)
    {
        var results = new Dictionary<string, object>();

        var defMethods = await QueryVerticesAsync(
            """
            SELECT properties FROM kg_vertices
            WHERE vertex_type = 'method' AND properties->>'name' = @needle
            """, ("needle", searchTerm));
        if (defMethods.Count > 0) results["def_methods"] = defMethods;

        var scriptMethods = await QueryVerticesAsync(
            """
            SELECT properties FROM kg_vertices
            WHERE vertex_type = 'script_method' AND properties->>'name' = @needle
            """, ("needle", searchTerm));
        if (scriptMethods.Count > 0) results["python_implementations"] = scriptMethods;

        var cppMethods = await QueryVerticesAsync(
            """
            SELECT properties FROM kg_vertices
            WHERE vertex_type = 'cpp_method' AND properties->>'name' = @needle
            """, ("needle", searchTerm));
        if (cppMethods.Count > 0) results["cpp_implementations"] = cppMethods;

        var props = await QueryVerticesAsync(
            """
            SELECT properties FROM kg_vertices
            WHERE vertex_type = 'property' AND properties->>'name' = @needle
            """, ("needle", searchTerm));
        if (props.Count > 0) results["properties"] = props;

        // Enum match — search both enum name and any token.
        var enumMatches = await QueryVerticesAsync(
            """
            SELECT properties FROM kg_vertices
            WHERE vertex_type = 'enumeration' AND (
                properties->>'name' ILIKE @needle
                OR EXISTS (
                    SELECT 1 FROM jsonb_object_keys(COALESCE(properties->'tokens', '{}'::jsonb)) k
                    WHERE k ILIKE @needle
                )
            )
            """, ("needle", "%" + searchTerm + "%"));
        if (enumMatches.Count > 0) results["enumerations"] = enumMatches;

        var constants = await QueryVerticesAsync(
            """
            SELECT properties FROM kg_vertices
            WHERE vertex_type = 'constant' AND properties->>'name' ILIKE @needle
            """, ("needle", "%" + searchTerm + "%"));
        if (constants.Count > 0) results["constants"] = constants;

        var gameDefs = await QueryVerticesAsync(
            """
            SELECT properties FROM kg_vertices
            WHERE vertex_type = 'game_def' AND properties->>'name' ILIKE @needle
            """, ("needle", "%" + searchTerm + "%"));
        if (gameDefs.Count > 0) results["game_definitions"] = gameDefs;

        var calls = await QueryEdgesByToMethodAsync(searchTerm);
        if (calls.Count > 0) results["called_by"] = calls;

        if (results.Count == 0)
        {
            return JsonSerializer.Serialize(new { query = searchTerm, message = "No matches found." });
        }
        results["query"] = searchTerm;
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    // ──────────────────────────────────────────────────────────────
    // Entity protocol
    // ──────────────────────────────────────────────────────────────

    public async Task<string> GetEntityProtocolAsync(string entityName)
    {
        var chain = new List<string> { entityName };
        var current = entityName;
        for (int i = 0; i < 10; i++)
        {
            var parents = await QueryEdgesByFromAndTypeAsync($"entity:{current}", "inherits");
            if (parents.Count == 0) break;
            var parent = ((string)parents[0]["to_id"]!).Replace("entity:", "");
            chain.Add(parent);
            current = parent;
        }

        var ifaceEdges = await QueryEdgesByFromAndTypeAsync($"entity:{entityName}", "implements");
        var interfaces = new List<string>();
        foreach (var edge in ifaceEdges)
        {
            var iface = ((string)edge["to_id"]!).Replace("entity:", "");
            interfaces.Add(iface);
            chain.Add(iface);
        }

        var clientMethods = new List<object>();
        var cellMethods = new List<object>();
        var baseMethods = new List<object>();
        var replicatedProps = new List<object>();

        foreach (var owner in chain)
        {
            var methods = await QueryVerticesByOwnerAndTypeAsync(owner, "method");
            foreach (var m in methods)
            {
                var props = m;
                var methodType = TryGetString(props, "method_type");
                var info = new
                {
                    name = TryGetString(props, "name"),
                    defined_in = owner,
                    exposed = TryGetBool(props, "exposed"),
                    arg_count = TryGetInt(props, "arg_count"),
                    args = TryGetField(props, "args"),
                };
                switch (methodType)
                {
                    case "client": clientMethods.Add(info); break;
                    case "cell": cellMethods.Add(info); break;
                    case "base": baseMethods.Add(info); break;
                }
            }

            var props2 = await QueryVerticesByOwnerAndTypeAsync(owner, "property");
            foreach (var p in props2)
            {
                if (!TryGetBool(p, "replicated")) continue;
                replicatedProps.Add(new
                {
                    name = TryGetString(p, "name"),
                    data_type = TryGetString(p, "data_type"),
                    flags = TryGetString(p, "flags"),
                    defined_in = owner,
                });
            }
        }

        var exposedBase = baseMethods.Where(m => ((dynamic)m).exposed == true).ToList();
        var internalBase = baseMethods.Where(m => ((dynamic)m).exposed != true).ToList();

        return JsonSerializer.Serialize(new
        {
            entity = entityName,
            inheritance_chain = chain,
            interfaces,
            protocol = new
            {
                server_to_client = new
                {
                    description = "Methods the server calls on the client (ClientMethods)",
                    methods = clientMethods,
                    count = clientMethods.Count,
                },
                client_to_server = new
                {
                    description = "Methods the client can call on the server (Exposed BaseMethods)",
                    methods = exposedBase,
                    count = exposedBase.Count,
                },
                server_internal_base = new
                {
                    description = "Internal server-side base methods (not client-callable)",
                    methods = internalBase,
                    count = internalBase.Count,
                },
                cell_methods = new
                {
                    description = "Server-side cell methods (gameplay logic)",
                    methods = cellMethods,
                    count = cellMethods.Count,
                },
                auto_replicated = new
                {
                    description = "Properties automatically replicated to client (CELL_PUBLIC etc)",
                    properties = replicatedProps,
                    count = replicatedProps.Count,
                },
            },
        }, JsonOptions);
    }

    // ──────────────────────────────────────────────────────────────
    // BigWorld API
    // ──────────────────────────────────────────────────────────────

    public async Task<string> LookupBigWorldApiAsync(string? apiName)
    {
        if (!string.IsNullOrEmpty(apiName))
        {
            var usages = await QueryEdgesAsync(
                """
                SELECT from_id, properties->>'label' AS label
                FROM kg_edges
                WHERE edge_type = 'uses_bigworld' AND properties->>'to_method' = @api
                """, ("api", apiName));

            var cppImpls = await QueryVerticesAsync(
                """
                SELECT properties FROM kg_vertices
                WHERE vertex_type = 'cpp_method' AND properties->>'name' ILIKE @needle
                """, ("needle", "%" + apiName + "%"));

            return JsonSerializer.Serialize(new
            {
                api = apiName,
                python_usages = usages,
                python_usage_count = usages.Count,
                cpp_implementations = cppImpls,
            }, JsonOptions);
        }

        var allApis = await ScalarGroupAsync(
            """
            SELECT properties->>'to_method' AS to_method, COUNT(*) AS usage_count
            FROM kg_edges WHERE edge_type = 'uses_bigworld'
            GROUP BY properties->>'to_method'
            """);

        return JsonSerializer.Serialize(new
        {
            bigworld_apis = allApis,
            total_apis = allApis.Count,
        }, JsonOptions);
    }

    // ──────────────────────────────────────────────────────────────
    // Postgres helpers — each method below maps a Cosmos query
    // pattern to one or two SQL templates. The patterns repeat enough
    // to make the helpers worth the boilerplate.
    // ──────────────────────────────────────────────────────────────

    private async Task<JsonElement?> GetVertexAsync(string id)
    {
        await using var cmd = _db.CreateCommand("SELECT properties FROM kg_vertices WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return JsonDocument.Parse(reader.GetString(0)).RootElement.Clone();
    }

    private async Task<JsonElement?> QuerySingleVertexAsync(string vertexType, string name)
    {
        await using var cmd = _db.CreateCommand(
            "SELECT properties FROM kg_vertices WHERE vertex_type = @t AND name = @n LIMIT 1");
        cmd.Parameters.AddWithValue("t", vertexType);
        cmd.Parameters.AddWithValue("n", name);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return JsonDocument.Parse(reader.GetString(0)).RootElement.Clone();
    }

    private async Task<List<JsonElement>> QueryVerticesByOwnerAndTypeAsync(string owner, string vertexType)
    {
        await using var cmd = _db.CreateCommand(
            """
            SELECT properties FROM kg_vertices
            WHERE vertex_type = @t AND properties->>'owner' = @owner
            """);
        cmd.Parameters.AddWithValue("t", vertexType);
        cmd.Parameters.AddWithValue("owner", owner);

        var rows = new List<JsonElement>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(JsonDocument.Parse(reader.GetString(0)).RootElement.Clone());
        }
        return rows;
    }

    private async Task<long> CountVerticesByOwnerAndTypeAsync(string owner, string vertexType)
    {
        await using var cmd = _db.CreateCommand(
            """
            SELECT COUNT(*) FROM kg_vertices
            WHERE vertex_type = @t AND properties->>'owner' = @owner
            """);
        cmd.Parameters.AddWithValue("t", vertexType);
        cmd.Parameters.AddWithValue("owner", owner);
        var v = await cmd.ExecuteScalarAsync();
        return v is long l ? l : Convert.ToInt64(v);
    }

    private async Task<List<object>> QueryVerticesByLabelAsync(string label, params string[] fields)
    {
        var fieldList = string.Join(", ", fields.Select(f => $"properties->>'{f}' AS \"{f}\""));
        await using var cmd = _db.CreateCommand(
            $"SELECT {fieldList} FROM kg_vertices WHERE vertex_type = @t");
        cmd.Parameters.AddWithValue("t", label);

        var rows = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < fields.Length; i++)
            {
                row[fields[i]] = reader.IsDBNull(i) ? null : reader.GetString(i);
            }
            rows.Add(row);
        }
        return rows;
    }

    private async Task<List<object>> QueryVerticesContainingNameAsync(string vertexType, string needle)
    {
        await using var cmd = _db.CreateCommand(
            """
            SELECT properties FROM kg_vertices
            WHERE vertex_type = @t AND properties->>'name' ILIKE @needle
            """);
        cmd.Parameters.AddWithValue("t", vertexType);
        cmd.Parameters.AddWithValue("needle", "%" + needle + "%");

        var rows = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(JsonDocument.Parse(reader.GetString(0)).RootElement.Clone());
        }
        return rows;
    }

    private async Task<List<Dictionary<string, object?>>> QueryVerticesAsync(
        string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = _db.CreateCommand(sql);
        foreach (var (n, v) in parameters)
        {
            cmd.Parameters.AddWithValue(n, v);
        }

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }
        return rows;
    }

    private async Task<List<Dictionary<string, object?>>> QueryEdgesByFromAsync(string fromId)
        => await QueryEdgesAsync(
            """
            SELECT id, from_id, to_id, edge_type, properties->>'label' AS label
            FROM kg_edges WHERE from_id = @id
            """,
            ("id", fromId));

    private async Task<List<Dictionary<string, object?>>> QueryEdgesByToAsync(string toId)
        => await QueryEdgesAsync(
            """
            SELECT id, from_id, to_id, edge_type, properties->>'label' AS label
            FROM kg_edges WHERE to_id = @id
            """,
            ("id", toId));

    private async Task<List<Dictionary<string, object?>>> QueryEdgesByFromAndTypeAsync(string fromId, string edgeType)
        => await QueryEdgesAsync(
            """
            SELECT id, from_id, to_id, edge_type, properties
            FROM kg_edges
            WHERE from_id = @id
              AND (edge_type = @t OR properties->>'label' = @t)
            """,
            ("id", fromId), ("t", edgeType));

    private async Task<List<Dictionary<string, object?>>> QueryEdgesByToAndTypeAsync(string toId, string edgeType)
        => await QueryEdgesAsync(
            """
            SELECT id, from_id, to_id, edge_type, properties
            FROM kg_edges
            WHERE to_id = @id
              AND (edge_type = @t OR properties->>'label' = @t)
            """,
            ("id", toId), ("t", edgeType));

    private async Task<List<Dictionary<string, object?>>> QueryEdgesByFromPrefixAndTypesAsync(
        string fromPrefix, string[] edgeTypes)
        => await QueryEdgesAsync(
            """
            SELECT id, from_id, to_id, edge_type, properties
            FROM kg_edges
            WHERE from_id LIKE @prefix
              AND (edge_type = ANY(@types) OR properties->>'label' = ANY(@types))
            """,
            ("prefix", fromPrefix + "%"), ("types", edgeTypes));

    private async Task<List<Dictionary<string, object?>>> QueryEdgesByToMethodAndTypesAsync(
        string toMethod, string[] edgeTypes)
        => await QueryEdgesAsync(
            """
            SELECT id, from_id, to_id, edge_type, properties
            FROM kg_edges
            WHERE properties->>'to_method' = @m
              AND (edge_type = ANY(@types) OR properties->>'label' = ANY(@types))
            """,
            ("m", toMethod), ("types", edgeTypes));

    private async Task<List<Dictionary<string, object?>>> QueryEdgesByToMethodAsync(string toMethod)
        => await QueryEdgesAsync(
            """
            SELECT properties->>'label' AS label, from_id, properties->>'to_method' AS to_method
            FROM kg_edges WHERE properties->>'to_method' = @m
            """,
            ("m", toMethod));

    private async Task<List<Dictionary<string, object?>>> QueryEdgesAsync(
        string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = _db.CreateCommand(sql);
        foreach (var (n, v) in parameters)
        {
            cmd.Parameters.AddWithValue(n, v);
        }

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                if (reader.IsDBNull(i)) { row[name] = null; continue; }
                // jsonb columns deserialise to string by default;
                // expand `properties` so callers can introspect.
                if (name == "properties")
                {
                    row[name] = JsonDocument.Parse(reader.GetString(i)).RootElement.Clone();
                }
                else
                {
                    row[name] = reader.GetValue(i);
                }
            }
            rows.Add(row);
        }
        return rows;
    }

    private async Task<List<object>> ScalarGroupAsync(string sql)
    {
        await using var cmd = _db.CreateCommand(sql);
        var rows = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new
            {
                key = reader.IsDBNull(0) ? null : reader.GetString(0),
                count = reader.GetInt64(1),
            });
        }
        return rows;
    }

    private async Task<long> ScalarLongAsync(string sql)
    {
        await using var cmd = _db.CreateCommand(sql);
        var v = await cmd.ExecuteScalarAsync();
        return v is long l ? l : Convert.ToInt64(v);
    }

    // ── JSON property accessors ───────────────────────────────────

    private static string? TryGetString(JsonElement el, string field) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static bool TryGetBool(JsonElement el, string field) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(field, out var v)
        && (v.ValueKind == JsonValueKind.True
            || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b));

    private static int TryGetInt(JsonElement el, string field) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : 0;

    private static object? TryGetField(JsonElement el, string field) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(field, out var v) ? v.Clone() : null;
}
