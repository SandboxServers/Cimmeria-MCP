using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CimmeriaMcp.Functions.Services;

public class CimmeriaGraphService
{
    private const string DatabaseName = "cimmeria";
    private const string ContainerName = "knowledge-graph";

    private readonly Container _container;

    private static string SafeId(string rawId) =>
        rawId.Replace("/", "--").Replace("\\", "--").Replace("#", "-").Replace("?", "-");

    public CimmeriaGraphService()
    {
        var cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")
            ?? throw new InvalidOperationException("COSMOS_ENDPOINT is not configured.");
        var cosmosKey = Environment.GetEnvironmentVariable("COSMOS_KEY")
            ?? throw new InvalidOperationException("COSMOS_KEY is not configured.");

        var cosmosClient = new CosmosClient(cosmosEndpoint, cosmosKey);
        _container = cosmosClient.GetContainer(DatabaseName, ContainerName);
    }

    /// <summary>
    /// Get full details about a specific entity or interface, including its properties, methods,
    /// parent, interfaces, and game system associations.
    /// </summary>
    public async Task<string> GetEntityDetailsAsync(string entityName)
    {
        // Get the entity vertex
        var entitySql = "SELECT * FROM c WHERE c.id = @id AND c.doc_type = 'vertex'";
        var entityDef = new QueryDefinition(entitySql)
            .WithParameter("@id", $"entity:{entityName}");

        dynamic? entity = null;
        using (var iter = _container.GetItemQueryIterator<dynamic>(entityDef))
        {
            while (iter.HasMoreResults)
            {
                var resp = await iter.ReadNextAsync();
                foreach (var item in resp)
                    entity = item;
            }
        }

        if (entity == null)
            return JsonConvert.SerializeObject(new { error = $"Entity '{entityName}' not found in knowledge graph." });

        // Get properties
        var propsSql = "SELECT * FROM c WHERE c.label = 'property' AND c.owner = @owner AND c.doc_type = 'vertex'";
        var props = await QueryListAsync<dynamic>(new QueryDefinition(propsSql)
            .WithParameter("@owner", entityName));

        // Get methods
        var methodsSql = "SELECT * FROM c WHERE c.label = 'method' AND c.owner = @owner AND c.doc_type = 'vertex'";
        var methods = await QueryListAsync<dynamic>(new QueryDefinition(methodsSql)
            .WithParameter("@owner", entityName));

        // Get edges from this entity
        var edgesSql = "SELECT * FROM c WHERE c.from_id = @entityId AND c.doc_type = 'edge'";
        var edges = await QueryListAsync<dynamic>(new QueryDefinition(edgesSql)
            .WithParameter("@entityId", $"entity:{entityName}"));

        // Get incoming edges (who inherits/implements this)
        var inEdgesSql = "SELECT * FROM c WHERE c.to_id = @entityId AND c.doc_type = 'edge'";
        var inEdges = await QueryListAsync<dynamic>(new QueryDefinition(inEdgesSql)
            .WithParameter("@entityId", $"entity:{entityName}"));

        return JsonConvert.SerializeObject(new
        {
            entity,
            properties = props,
            methods,
            outgoing_edges = edges,
            incoming_edges = inEdges,
        }, Formatting.Indented);
    }

    /// <summary>
    /// Traverse relationships from a starting entity, following specified edge types.
    /// </summary>
    public async Task<string> TraverseGraphAsync(string startEntity, string edgeType, int depth)
    {
        var visited = new HashSet<string>();
        var results = new List<object>();
        var frontier = new Queue<(string entityId, int currentDepth)>();
        frontier.Enqueue(($"entity:{startEntity}", 0));
        visited.Add($"entity:{startEntity}");

        while (frontier.Count > 0)
        {
            var (currentId, currentDepth) = frontier.Dequeue();
            if (currentDepth >= depth)
                continue;

            // Find edges of the specified type from current node
            var sql = "SELECT * FROM c WHERE c.from_id = @fromId AND c.label = @label AND c.doc_type = 'edge'";
            var queryDef = new QueryDefinition(sql)
                .WithParameter("@fromId", currentId)
                .WithParameter("@label", edgeType);

            var edges = await QueryListAsync<dynamic>(queryDef);
            foreach (var edge in edges)
            {
                string toId = (string)edge.to_id;
                results.Add(new
                {
                    from = currentId,
                    edge_type = edgeType,
                    to = toId,
                    depth = currentDepth + 1,
                });

                if (!visited.Contains(toId))
                {
                    visited.Add(toId);
                    frontier.Enqueue((toId, currentDepth + 1));
                }
            }

            // Also check reverse direction for some edge types
            if (edgeType is "inherits" or "implements" or "python_inherits")
            {
                var revSql = "SELECT * FROM c WHERE c.to_id = @toId AND c.label = @label AND c.doc_type = 'edge'";
                var revDef = new QueryDefinition(revSql)
                    .WithParameter("@toId", currentId)
                    .WithParameter("@label", edgeType);

                var revEdges = await QueryListAsync<dynamic>(revDef);
                foreach (var edge in revEdges)
                {
                    string fromId = (string)edge.from_id;
                    results.Add(new
                    {
                        from = fromId,
                        edge_type = edgeType,
                        to = currentId,
                        depth = currentDepth + 1,
                    });

                    if (!visited.Contains(fromId))
                    {
                        visited.Add(fromId);
                        frontier.Enqueue((fromId, currentDepth + 1));
                    }
                }
            }
        }

        return JsonConvert.SerializeObject(new
        {
            start = startEntity,
            edge_type = edgeType,
            max_depth = depth,
            traversal = results,
            nodes_visited = visited.Count,
        }, Formatting.Indented);
    }

    /// <summary>
    /// Get the full inheritance hierarchy for an entity — what it inherits from and what inherits from it.
    /// </summary>
    public async Task<string> GetInheritanceTreeAsync(string entityName)
    {
        // Walk up the inheritance chain
        var ancestors = new List<string>();
        var current = entityName;
        for (int i = 0; i < 10; i++) // safety limit
        {
            var sql = "SELECT * FROM c WHERE c.from_id = @fromId AND c.label = 'inherits' AND c.doc_type = 'edge'";
            var edges = await QueryListAsync<dynamic>(new QueryDefinition(sql)
                .WithParameter("@fromId", $"entity:{current}"));

            if (edges.Count == 0) break;
            string parent = ((string)edges[0].to_id).Replace("entity:", "");
            ancestors.Add(parent);
            current = parent;
        }

        // Find descendants (who inherits from this entity)
        var descendants = new List<object>();
        var descSql = "SELECT * FROM c WHERE c.to_id = @toId AND c.label = 'inherits' AND c.doc_type = 'edge'";
        var descEdges = await QueryListAsync<dynamic>(new QueryDefinition(descSql)
            .WithParameter("@toId", $"entity:{entityName}"));

        foreach (var edge in descEdges)
        {
            string child = ((string)edge.from_id).Replace("entity:", "");
            descendants.Add(new { entity = child });
        }

        // Interfaces implemented
        var ifaceSql = "SELECT * FROM c WHERE c.from_id = @fromId AND c.label = 'implements' AND c.doc_type = 'edge'";
        var ifaceEdges = await QueryListAsync<dynamic>(new QueryDefinition(ifaceSql)
            .WithParameter("@fromId", $"entity:{entityName}"));
        var interfaces = ifaceEdges.Select(e => ((string)e.to_id).Replace("entity:", "")).ToList();

        return JsonConvert.SerializeObject(new
        {
            entity = entityName,
            ancestors,
            descendants,
            interfaces,
        }, Formatting.Indented);
    }

    /// <summary>
    /// Get graph overview — vertex/edge counts by type.
    /// </summary>
    public async Task<string> GetGraphOverviewAsync()
    {
        var vertexSql = "SELECT c.label, COUNT(1) AS count FROM c WHERE c.doc_type = 'vertex' GROUP BY c.label";
        var vertexCounts = await QueryListAsync<dynamic>(new QueryDefinition(vertexSql));

        var edgeSql = "SELECT c.label, COUNT(1) AS count FROM c WHERE c.doc_type = 'edge' GROUP BY c.label";
        var edgeCounts = await QueryListAsync<dynamic>(new QueryDefinition(edgeSql));

        // List all entities
        var entitiesSql = "SELECT c.name FROM c WHERE c.label = 'entity' AND c.doc_type = 'vertex'";
        var entities = await QueryListAsync<dynamic>(new QueryDefinition(entitiesSql));

        var interfacesSql = "SELECT c.name FROM c WHERE c.label = 'interface' AND c.doc_type = 'vertex'";
        var interfacesResult = await QueryListAsync<dynamic>(new QueryDefinition(interfacesSql));

        var systemsSql = "SELECT c.name, c.description FROM c WHERE c.label = 'game_system' AND c.doc_type = 'vertex'";
        var systems = await QueryListAsync<dynamic>(new QueryDefinition(systemsSql));

        return JsonConvert.SerializeObject(new
        {
            vertex_counts = vertexCounts,
            edge_counts = edgeCounts,
            entities,
            interfaces = interfacesResult,
            game_systems = systems,
        }, Formatting.Indented);
    }

    /// <summary>
    /// Find all entities and methods related to a specific game system.
    /// </summary>
    public async Task<string> GetGameSystemDetailsAsync(string systemName)
    {
        // Get the system vertex
        var sysSql = "SELECT * FROM c WHERE c.id = @id AND c.doc_type = 'vertex'";
        var sysResult = await QueryListAsync<dynamic>(new QueryDefinition(sysSql)
            .WithParameter("@id", $"system:{systemName}"));

        if (sysResult.Count == 0)
            return JsonConvert.SerializeObject(new { error = $"Game system '{systemName}' not found." });

        // Get entities linked to this system
        var linkSql = "SELECT * FROM c WHERE c.to_id = @sysId AND c.label = 'part_of_system' AND c.doc_type = 'edge'";
        var links = await QueryListAsync<dynamic>(new QueryDefinition(linkSql)
            .WithParameter("@sysId", $"system:{systemName}"));

        var entityDetails = new List<object>();
        foreach (var link in links)
        {
            string entityId = (string)link.from_id;
            string entityName = entityId.Replace("entity:", "");

            // Get property and method counts for each linked entity
            var propsSql = "SELECT VALUE COUNT(1) FROM c WHERE c.label = 'property' AND c.owner = @owner AND c.doc_type = 'vertex'";
            var propCount = await QueryScalarAsync<long>(new QueryDefinition(propsSql)
                .WithParameter("@owner", entityName));

            var methodsSql = "SELECT VALUE COUNT(1) FROM c WHERE c.label = 'method' AND c.owner = @owner AND c.doc_type = 'vertex'";
            var methodCount = await QueryScalarAsync<long>(new QueryDefinition(methodsSql)
                .WithParameter("@owner", entityName));

            entityDetails.Add(new
            {
                entity = entityName,
                property_count = propCount,
                method_count = methodCount,
            });
        }

        return JsonConvert.SerializeObject(new
        {
            system = sysResult[0],
            entities = entityDetails,
        }, Formatting.Indented);
    }

    /// <summary>
    /// Find all client-visible (replicated) properties for an entity and its full inheritance chain.
    /// </summary>
    public async Task<string> GetReplicatedPropertiesAsync(string entityName)
    {
        // Collect entity + all ancestors
        var chain = new List<string> { entityName };
        var current = entityName;
        for (int i = 0; i < 10; i++)
        {
            var sql = "SELECT * FROM c WHERE c.from_id = @fromId AND c.label = 'inherits' AND c.doc_type = 'edge'";
            var edges = await QueryListAsync<dynamic>(new QueryDefinition(sql)
                .WithParameter("@fromId", $"entity:{current}"));
            if (edges.Count == 0) break;
            string parent = ((string)edges[0].to_id).Replace("entity:", "");
            chain.Add(parent);
            current = parent;
        }

        // Get interfaces
        var ifaceSql = "SELECT c.to_id FROM c WHERE c.from_id = @fromId AND c.label = 'implements' AND c.doc_type = 'edge'";
        var ifaceEdges = await QueryListAsync<dynamic>(new QueryDefinition(ifaceSql)
            .WithParameter("@fromId", $"entity:{entityName}"));
        foreach (var edge in ifaceEdges)
            chain.Add(((string)edge.to_id).Replace("entity:", ""));

        // Get all replicated properties across the chain
        var allProps = new List<object>();
        foreach (var owner in chain)
        {
            var propsSql = "SELECT * FROM c WHERE c.label = 'property' AND c.owner = @owner AND c.replicated = true AND c.doc_type = 'vertex'";
            var props = await QueryListAsync<dynamic>(new QueryDefinition(propsSql)
                .WithParameter("@owner", owner));

            foreach (var prop in props)
            {
                allProps.Add(new
                {
                    name = (string)prop.name,
                    data_type = (string)prop.data_type,
                    flags = (string)prop.flags,
                    defined_in = owner,
                });
            }
        }

        return JsonConvert.SerializeObject(new
        {
            entity = entityName,
            inheritance_chain = chain,
            replicated_properties = allProps,
        }, Formatting.Indented);
    }

    /// <summary>
    /// Find method call chains — what does a specific method call, and what calls it.
    /// </summary>
    public async Task<string> GetMethodCallChainAsync(string entityName, string methodName)
    {
        // Find outgoing calls from this method's script implementation
        var outSql = "SELECT * FROM c WHERE STARTSWITH(c.from_id, @prefix) AND c.doc_type = 'edge' " +
                     "AND c.label IN ('sends_to_client', 'sends_to_base', 'sends_to_cell', 'calls_self', 'calls_super', 'uses_bigworld')";
        var outEdges = await QueryListAsync<dynamic>(new QueryDefinition(outSql)
            .WithParameter("@prefix", $"script_method:cell:{entityName}.{methodName}"));

        // Also check base script
        var outBaseEdges = await QueryListAsync<dynamic>(new QueryDefinition(outSql)
            .WithParameter("@prefix", $"script_method:base:{entityName}.{methodName}"));

        // Find what calls this method
        var inSql = "SELECT * FROM c WHERE c.to_method = @method AND c.doc_type = 'edge' " +
                    "AND c.label IN ('sends_to_client', 'sends_to_base', 'sends_to_cell', 'calls_self')";
        var inEdges = await QueryListAsync<dynamic>(new QueryDefinition(inSql)
            .WithParameter("@method", methodName));

        return JsonConvert.SerializeObject(new
        {
            entity = entityName,
            method = methodName,
            outgoing_calls = outEdges.Concat(outBaseEdges).ToList(),
            incoming_calls = inEdges,
        }, Formatting.Indented);
    }

    // ====================================================================
    // Enhancement #1: Enum & Type Resolver
    // ====================================================================

    public async Task<string> LookupEnumAsync(string enumName, string? tokenName)
    {
        if (!string.IsNullOrEmpty(tokenName))
        {
            // Search for a specific token across all enums
            var sql = "SELECT * FROM c WHERE c.label = 'enumeration' AND c.doc_type = 'vertex'";
            var enums = await QueryListAsync<dynamic>(new QueryDefinition(sql));

            var matches = new List<object>();
            foreach (var e in enums)
            {
                var tokens = e.tokens;
                if (tokens != null)
                {
                    foreach (var prop in tokens)
                    {
                        string tName = prop.Name;
                        if (tName.Contains(tokenName, StringComparison.OrdinalIgnoreCase))
                        {
                            matches.Add(new { enumeration = (string)e.name, token = tName, value = (string)prop.Value });
                        }
                    }
                }
            }
            return JsonConvert.SerializeObject(new { query = tokenName, matches, count = matches.Count }, Formatting.Indented);
        }

        // Look up a specific enumeration by name
        var enumSql = "SELECT * FROM c WHERE c.label = 'enumeration' AND c.name = @name AND c.doc_type = 'vertex'";
        var result = await QueryListAsync<dynamic>(new QueryDefinition(enumSql)
            .WithParameter("@name", enumName));

        if (result.Count == 0)
        {
            // Try searching constants
            var constSql = "SELECT * FROM c WHERE c.label = 'constant' AND CONTAINS(c.name, @name, true) AND c.doc_type = 'vertex'";
            var constants = await QueryListAsync<dynamic>(new QueryDefinition(constSql)
                .WithParameter("@name", enumName));
            return JsonConvert.SerializeObject(new { query = enumName, constants, count = constants.Count }, Formatting.Indented);
        }

        return JsonConvert.SerializeObject(new { enumeration = result[0] }, Formatting.Indented);
    }

    public async Task<string> ResolveTypeAsync(string typeName)
    {
        var sql = "SELECT * FROM c WHERE c.label = 'type_alias' AND c.name = @name AND c.doc_type = 'vertex'";
        var result = await QueryListAsync<dynamic>(new QueryDefinition(sql)
            .WithParameter("@name", typeName));

        if (result.Count == 0)
            return JsonConvert.SerializeObject(new { error = $"Type '{typeName}' not found." });

        return JsonConvert.SerializeObject(new { type_definition = result[0] }, Formatting.Indented);
    }

    // ====================================================================
    // Enhancement #2: Game Data Definition Lookup
    // ====================================================================

    public async Task<string> LookupGameDefAsync(string defName)
    {
        var sql = "SELECT * FROM c WHERE c.label = 'game_def' AND c.name = @name AND c.doc_type = 'vertex'";
        var result = await QueryListAsync<dynamic>(new QueryDefinition(sql)
            .WithParameter("@name", defName));

        if (result.Count == 0)
        {
            // List all available game defs
            var listSql = "SELECT c.name, c.field_count, c.source_file FROM c WHERE c.label = 'game_def' AND c.doc_type = 'vertex'";
            var allDefs = await QueryListAsync<dynamic>(new QueryDefinition(listSql));
            return JsonConvert.SerializeObject(new
            {
                error = $"Game definition '{defName}' not found.",
                available_defs = allDefs,
            }, Formatting.Indented);
        }

        // Get cross-references
        var refSql = "SELECT * FROM c WHERE c.label = 'references_def' AND STARTSWITH(c.from_id, @prefix) AND c.doc_type = 'edge'";
        var refs = await QueryListAsync<dynamic>(new QueryDefinition(refSql)
            .WithParameter("@prefix", $"game_def:{defName}"));

        return JsonConvert.SerializeObject(new
        {
            definition = result[0],
            cross_references = refs,
        }, Formatting.Indented);
    }

    // ====================================================================
    // Enhancement #3: Implementation Coverage / Gap Analysis
    // ====================================================================

    public async Task<string> GetImplementationStatusAsync(string? entityName)
    {
        if (!string.IsNullOrEmpty(entityName))
        {
            // Get defined methods for this entity
            var defMethodsSql = "SELECT c.name, c.method_type FROM c WHERE c.label = 'method' AND c.owner = @owner AND c.doc_type = 'vertex'";
            var defMethods = await QueryListAsync<dynamic>(new QueryDefinition(defMethodsSql)
                .WithParameter("@owner", entityName));

            // Get Python implementations
            var pyMethodsSql = "SELECT c.name FROM c WHERE c.label = 'script_method' AND c.owner_class = @owner AND c.doc_type = 'vertex'";
            var pyMethods = await QueryListAsync<dynamic>(new QueryDefinition(pyMethodsSql)
                .WithParameter("@owner", entityName));

            // Get C++ implementations (search for methods that call Python methods matching entity methods)
            var cppMethodsSql = "SELECT c.name, c.owner_class, c.source_file FROM c WHERE c.label = 'cpp_method' AND c.doc_type = 'vertex'";
            var cppMethods = await QueryListAsync<dynamic>(new QueryDefinition(cppMethodsSql));

            var defMethodNames = new HashSet<string>();
            var defMethodDetails = new List<object>();
            foreach (var m in defMethods)
            {
                string name = (string)m.name;
                defMethodNames.Add(name);
                defMethodDetails.Add(new { name, method_type = (string)m.method_type });
            }

            var pyMethodNames = new HashSet<string>();
            foreach (var m in pyMethods)
                pyMethodNames.Add((string)m.name);

            var cppMethodNames = new HashSet<string>();
            foreach (var m in cppMethods)
                cppMethodNames.Add((string)m.name);

            var coverage = new List<object>();
            foreach (var m in defMethodDetails)
            {
                string name = (string)((dynamic)m).name;
                coverage.Add(new
                {
                    method = name,
                    method_type = (string)((dynamic)m).method_type,
                    has_python = pyMethodNames.Contains(name),
                    has_cpp = cppMethodNames.Contains(name),
                });
            }

            return JsonConvert.SerializeObject(new
            {
                entity = entityName,
                total_defined = defMethodNames.Count,
                python_implemented = pyMethodNames.Intersect(defMethodNames).Count(),
                cpp_implemented = cppMethodNames.Intersect(defMethodNames).Count(),
                methods = coverage,
            }, Formatting.Indented);
        }

        // Overview of all entities
        var entitiesSql = "SELECT c.name, c.property_count, c.client_method_count, c.cell_method_count, c.base_method_count FROM c WHERE c.label = 'entity' AND c.doc_type = 'vertex'";
        var entities = await QueryListAsync<dynamic>(new QueryDefinition(entitiesSql));

        // C++ class count and component breakdown
        var cppClassSql = "SELECT c.component, COUNT(1) AS count FROM c WHERE c.label = 'cpp_class' AND c.doc_type = 'vertex' GROUP BY c.component";
        var cppClasses = await QueryListAsync<dynamic>(new QueryDefinition(cppClassSql));

        var cppTotalSql = "SELECT VALUE COUNT(1) FROM c WHERE c.label = 'cpp_method' AND c.doc_type = 'vertex'";
        var cppTotal = await QueryScalarAsync<long>(new QueryDefinition(cppTotalSql));

        return JsonConvert.SerializeObject(new
        {
            entities,
            cpp_classes_by_component = cppClasses,
            total_cpp_methods = cppTotal,
            note = "Use entity_name parameter for per-entity method coverage",
        }, Formatting.Indented);
    }

    // ====================================================================
    // Enhancement #4: Cross-Reference Search
    // ====================================================================

    public async Task<string> CrossReferenceAsync(string searchTerm)
    {
        var results = new Dictionary<string, object>();

        // Search entity methods
        var methodSql = "SELECT c.name, c.owner, c.method_type FROM c WHERE c.label = 'method' AND c.name = @name AND c.doc_type = 'vertex'";
        var methods = await QueryListAsync<dynamic>(new QueryDefinition(methodSql)
            .WithParameter("@name", searchTerm));
        if (methods.Count > 0) results["def_methods"] = methods;

        // Search script methods
        var scriptSql = "SELECT c.name, c.owner_class, c.script_type, c.line FROM c WHERE c.label = 'script_method' AND c.name = @name AND c.doc_type = 'vertex'";
        var scripts = await QueryListAsync<dynamic>(new QueryDefinition(scriptSql)
            .WithParameter("@name", searchTerm));
        if (scripts.Count > 0) results["python_implementations"] = scripts;

        // Search C++ methods
        var cppSql = "SELECT c.name, c.owner_class, c.source_file FROM c WHERE c.label = 'cpp_method' AND c.name = @name AND c.doc_type = 'vertex'";
        var cppMethods = await QueryListAsync<dynamic>(new QueryDefinition(cppSql)
            .WithParameter("@name", searchTerm));
        if (cppMethods.Count > 0) results["cpp_implementations"] = cppMethods;

        // Search properties
        var propSql = "SELECT c.name, c.owner, c.data_type, c.flags, c.replicated FROM c WHERE c.label = 'property' AND c.name = @name AND c.doc_type = 'vertex'";
        var props = await QueryListAsync<dynamic>(new QueryDefinition(propSql)
            .WithParameter("@name", searchTerm));
        if (props.Count > 0) results["properties"] = props;

        // Search enumerations by token name
        var enumSql = "SELECT * FROM c WHERE c.label = 'enumeration' AND c.doc_type = 'vertex'";
        var allEnums = await QueryListAsync<dynamic>(new QueryDefinition(enumSql));
        var enumMatches = new List<object>();
        foreach (var e in allEnums)
        {
            if (((string)e.name).Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                enumMatches.Add(new { name = (string)e.name, token_count = (int)e.token_count });
            else if (e.tokens != null)
            {
                foreach (var t in e.tokens)
                {
                    if (((string)t.Name).Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    {
                        enumMatches.Add(new { enumeration = (string)e.name, token = (string)t.Name, value = (string)t.Value });
                        break; // One match per enum is enough
                    }
                }
            }
        }
        if (enumMatches.Count > 0) results["enumerations"] = enumMatches;

        // Search constants
        var constSql = "SELECT c.name, c[\"value\"] FROM c WHERE c.label = 'constant' AND CONTAINS(c.name, @name, true) AND c.doc_type = 'vertex'";
        var constants = await QueryListAsync<dynamic>(new QueryDefinition(constSql)
            .WithParameter("@name", searchTerm));
        if (constants.Count > 0) results["constants"] = constants;

        // Search game defs
        var gdSql = "SELECT c.name, c.field_count FROM c WHERE c.label = 'game_def' AND CONTAINS(c.name, @name, true) AND c.doc_type = 'vertex'";
        var gameDefs = await QueryListAsync<dynamic>(new QueryDefinition(gdSql)
            .WithParameter("@name", searchTerm));
        if (gameDefs.Count > 0) results["game_definitions"] = gameDefs;

        // Search edges (what calls/sends to this method)
        var callSql = "SELECT c.label, c.from_id, c.to_method FROM c WHERE c.to_method = @name AND c.doc_type = 'edge'";
        var calls = await QueryListAsync<dynamic>(new QueryDefinition(callSql)
            .WithParameter("@name", searchTerm));
        if (calls.Count > 0) results["called_by"] = calls;

        if (results.Count == 0)
            return JsonConvert.SerializeObject(new { query = searchTerm, message = "No matches found." });

        results["query"] = searchTerm;
        return JsonConvert.SerializeObject(results, Formatting.Indented);
    }

    // ====================================================================
    // Enhancement #5: Protocol Message Map
    // ====================================================================

    public async Task<string> GetEntityProtocolAsync(string entityName)
    {
        // Collect the full inheritance chain + interfaces
        var chain = new List<string> { entityName };
        var current = entityName;
        for (int i = 0; i < 10; i++)
        {
            var sql = "SELECT * FROM c WHERE c.from_id = @fromId AND c.label = 'inherits' AND c.doc_type = 'edge'";
            var edges = await QueryListAsync<dynamic>(new QueryDefinition(sql)
                .WithParameter("@fromId", $"entity:{current}"));
            if (edges.Count == 0) break;
            string parent = ((string)edges[0].to_id).Replace("entity:", "");
            chain.Add(parent);
            current = parent;
        }

        // Get interfaces
        var ifaceSql = "SELECT c.to_id FROM c WHERE c.from_id = @fromId AND c.label = 'implements' AND c.doc_type = 'edge'";
        var ifaceEdges = await QueryListAsync<dynamic>(new QueryDefinition(ifaceSql)
            .WithParameter("@fromId", $"entity:{entityName}"));
        var interfaces = new List<string>();
        foreach (var edge in ifaceEdges)
        {
            string iface = ((string)edge.to_id).Replace("entity:", "");
            interfaces.Add(iface);
            chain.Add(iface);
        }

        // Collect all methods across the chain
        var clientMethods = new List<object>();
        var cellMethods = new List<object>();
        var baseMethods = new List<object>();
        var replicatedProps = new List<object>();

        foreach (var owner in chain)
        {
            var methodsSql = "SELECT * FROM c WHERE c.label = 'method' AND c.owner = @owner AND c.doc_type = 'vertex'";
            var methods = await QueryListAsync<dynamic>(new QueryDefinition(methodsSql)
                .WithParameter("@owner", owner));

            foreach (var m in methods)
            {
                string methodType = (string)m.method_type;
                var info = new { name = (string)m.name, defined_in = owner, exposed = (bool)m.exposed, arg_count = (int)m.arg_count, args = m.args };
                if (methodType == "client") clientMethods.Add(info);
                else if (methodType == "cell") cellMethods.Add(info);
                else if (methodType == "base") baseMethods.Add(info);
            }

            var propsSql = "SELECT c.name, c.data_type, c.flags FROM c WHERE c.label = 'property' AND c.owner = @owner AND c.replicated = true AND c.doc_type = 'vertex'";
            var props = await QueryListAsync<dynamic>(new QueryDefinition(propsSql)
                .WithParameter("@owner", owner));
            foreach (var p in props)
                replicatedProps.Add(new { name = (string)p.name, data_type = (string)p.data_type, flags = (string)p.flags, defined_in = owner });
        }

        // Separate exposed base methods (client-callable) from internal
        var exposedBaseMethods = baseMethods.Where(m => (bool)((dynamic)m).exposed).ToList();
        var internalBaseMethods = baseMethods.Where(m => !(bool)((dynamic)m).exposed).ToList();

        return JsonConvert.SerializeObject(new
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
                    methods = exposedBaseMethods,
                    count = exposedBaseMethods.Count,
                },
                server_internal_base = new
                {
                    description = "Internal server-side base methods (not client-callable)",
                    methods = internalBaseMethods,
                    count = internalBaseMethods.Count,
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
        }, Formatting.Indented);
    }

    // ====================================================================
    // Enhancement #6: BigWorld API Reference
    // ====================================================================

    public async Task<string> LookupBigWorldApiAsync(string? apiName)
    {
        if (!string.IsNullOrEmpty(apiName))
        {
            // Find all usages of this BigWorld API
            var sql = "SELECT c.from_id, c.label FROM c WHERE c.label = 'uses_bigworld' AND c.to_method = @name AND c.doc_type = 'edge'";
            var usages = await QueryListAsync<dynamic>(new QueryDefinition(sql)
                .WithParameter("@name", apiName));

            // Also search C++ implementations
            var cppSql = "SELECT c.name, c.owner_class, c.source_file FROM c WHERE c.label = 'cpp_method' AND CONTAINS(c.name, @name, true) AND c.doc_type = 'vertex'";
            var cppImpls = await QueryListAsync<dynamic>(new QueryDefinition(cppSql)
                .WithParameter("@name", apiName));

            return JsonConvert.SerializeObject(new
            {
                api = apiName,
                python_usages = usages,
                python_usage_count = usages.Count,
                cpp_implementations = cppImpls,
            }, Formatting.Indented);
        }

        // List all BigWorld APIs used across the codebase
        var allSql = "SELECT c.to_method, COUNT(1) AS usage_count FROM c WHERE c.label = 'uses_bigworld' AND c.doc_type = 'edge' GROUP BY c.to_method";
        var allApis = await QueryListAsync<dynamic>(new QueryDefinition(allSql));

        return JsonConvert.SerializeObject(new
        {
            bigworld_apis = allApis,
            total_apis = allApis.Count,
        }, Formatting.Indented);
    }

    private async Task<List<dynamic>> QueryListAsync<T>(QueryDefinition queryDef)
    {
        var items = new List<dynamic>();
        using var iter = _container.GetItemQueryIterator<dynamic>(queryDef, requestOptions: new QueryRequestOptions
        {
            MaxItemCount = 1000,
        });
        while (iter.HasMoreResults)
        {
            var resp = await iter.ReadNextAsync();
            items.AddRange(resp);
        }
        return items;
    }

    private async Task<T> QueryScalarAsync<T>(QueryDefinition queryDef)
    {
        using var iter = _container.GetItemQueryIterator<T>(queryDef);
        while (iter.HasMoreResults)
        {
            var resp = await iter.ReadNextAsync();
            foreach (var item in resp)
                return item;
        }
        return default!;
    }
}
