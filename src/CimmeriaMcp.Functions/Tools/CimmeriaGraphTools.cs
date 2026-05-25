using System.ComponentModel;
using CimmeriaMcp.Services;
using ModelContextProtocol.Server;

namespace CimmeriaMcp.Tools;

/// <summary>
/// Knowledge-graph MCP tools — entities, methods, inheritance,
/// game systems, enums, BigWorld APIs, cross-references. Discovered
/// + dispatched by the official MCP C# SDK.
/// </summary>
[McpServerToolType]
public sealed class CimmeriaGraphTools
{
    private readonly CimmeriaGraphService _graph;

    public CimmeriaGraphTools(CimmeriaGraphService graph)
    {
        _graph = graph;
    }

    [McpServerTool(Name = "get_entity_details")]
    [Description("Get full details about an SGW entity or interface from the knowledge graph — properties, methods, inheritance, interfaces, and game system links. Entity names: Account, SGWBeing, SGWBlackMarket, SGWChannelManager, SGWCoverSet, SGWDuelMarker, SGWEntity, SGWEscrow, SGWGmPlayer, SGWMob, SGWPet, SGWPlayer, SGWPlayerGroupAuthority, SGWPlayerRespawner, SGWSpaceCreator, SGWSpawnRegion, SGWSpawnSet, SGWSpawnableEntity. Interface names: ClientCache, Communicator, ContactListManager, DistributionGroupMember, EventParticipant, GateTravel, GroupAuthority, Lootable, MinigamePlayer, Missionary, OrganizationMember, SGWAbilityManager, SGWBeing, SGWBlackMarketManager, SGWCombatant, SGWInventoryManager, SGWMailManager, SGWPoller.")]
    public Task<string> GetEntityDetails(
        [Description("Entity or interface name e.g. SGWPlayer, SGWCombatant")] string entityName)
        => _graph.GetEntityDetailsAsync(entityName);

    [McpServerTool(Name = "get_inheritance_tree")]
    [Description("Get the full inheritance hierarchy for an entity — ancestors, descendants, and implemented interfaces.")]
    public Task<string> GetInheritanceTree(
        [Description("Entity name e.g. SGWPlayer")] string entityName)
        => _graph.GetInheritanceTreeAsync(entityName);

    [McpServerTool(Name = "get_graph_overview")]
    [Description("Get an overview of the SGW knowledge graph — vertex/edge counts, all entities, interfaces, and game systems.")]
    public Task<string> GetGraphOverview()
        => _graph.GetGraphOverviewAsync();

    [McpServerTool(Name = "get_game_system_details")]
    [Description("Get details about a specific game system and its associated entities. Systems: combat, inventory, missions, chat, spawning, gate_travel, organizations, trading, groups, account, minigame, cover.")]
    public Task<string> GetGameSystemDetails(
        [Description("Game system name e.g. combat, inventory, gate_travel")] string systemName)
        => _graph.GetGameSystemDetailsAsync(systemName);

    [McpServerTool(Name = "get_replicated_properties")]
    [Description("Get all client-visible (CELL_PUBLIC) properties for an entity, including inherited properties from the full inheritance chain and implemented interfaces.")]
    public Task<string> GetReplicatedProperties(
        [Description("Entity name e.g. SGWPlayer, SGWMob")] string entityName)
        => _graph.GetReplicatedPropertiesAsync(entityName);

    [McpServerTool(Name = "get_method_call_chain")]
    [Description("Trace method call chains — what a specific method calls (client RPC, base/cell messages, BigWorld APIs) and what calls it.")]
    public Task<string> GetMethodCallChain(
        [Description("Entity name e.g. SGWMob")] string entityName,
        [Description("Method name e.g. threatGenerated, doAiAction")] string methodName)
        => _graph.GetMethodCallChainAsync(entityName, methodName);

    [McpServerTool(Name = "traverse_graph")]
    [Description("Traverse the knowledge graph from a starting entity following a specific edge type. Edge types: inherits, implements, has_property, has_client_method, has_cell_method, has_base_method, part_of_system, defined_in, implements_def, python_inherits, sends_to_client, sends_to_base, calls_self.")]
    public Task<string> TraverseGraph(
        [Description("Starting entity name")] string startEntity,
        [Description("Edge type to follow e.g. inherits, implements, sends_to_client")] string edgeType,
        [Description("Max traversal depth (1-5, default 2)")] int? depth = null)
    {
        var d = Math.Clamp(depth ?? 2, 1, 5);
        return _graph.TraverseGraphAsync(startEntity, edgeType, d);
    }

    [McpServerTool(Name = "lookup_enum")]
    [Description("Look up SGW enumerations and constants. Search by enum name (e.g. EAbilityFlags, EDamageType) to get all tokens/values, or search by token name to find which enum it belongs to.")]
    public Task<string> LookupEnum(
        [Description("Enumeration name e.g. EAbilityFlags, EDamageType, EArchetype")] string? enumName = null,
        [Description("Search for a token/constant by name e.g. ARCHETYPE_Soldier, GENDER_Male")] string? tokenName = null)
        => _graph.LookupEnumAsync(enumName ?? "", tokenName);

    [McpServerTool(Name = "resolve_type")]
    [Description("Resolve a BigWorld type alias to its full structure. Handles simple aliases (CONTROLLER_ID → INT32), arrays (CharacterInfoList → ARRAY<CharacterInfo>), and FIXED_DICT compound types.")]
    public Task<string> ResolveType(
        [Description("Type name e.g. CharacterInfo, StatList, LootItemQuantity")] string typeName)
        => _graph.ResolveTypeAsync(typeName);

    [McpServerTool(Name = "lookup_game_def")]
    [Description("Look up a game data definition schema — fields, cross-references to other defs, and enum references.")]
    public Task<string> LookupGameDef(
        [Description("Definition class name e.g. Ability, Item, Mission, LootTable")] string defName)
        => _graph.LookupGameDefAsync(defName);

    [McpServerTool(Name = "get_implementation_status")]
    [Description("Analyze implementation coverage — compare .def method definitions against Python and C++ implementations.")]
    public Task<string> GetImplementationStatus(
        [Description("Entity name for per-method analysis, or omit for overview")] string? entityName = null)
        => _graph.GetImplementationStatusAsync(entityName);

    [McpServerTool(Name = "cross_reference")]
    [Description("Search across the entire knowledge graph for a term — finds matching methods (def/Python/C++), properties, enumerations, constants, game definitions, and call chains.")]
    public Task<string> CrossReference(
        [Description("Term to search for e.g. threatGenerated, Alignment, ARCHETYPE")] string searchTerm)
        => _graph.CrossReferenceAsync(searchTerm);

    [McpServerTool(Name = "get_entity_protocol")]
    [Description("Get the complete client-server protocol for an entity — all client methods (server→client), exposed base methods (client→server), cell methods (gameplay logic), and auto-replicated properties.")]
    public Task<string> GetEntityProtocol(
        [Description("Entity name e.g. SGWPlayer, Account, SGWMob")] string entityName)
        => _graph.GetEntityProtocolAsync(entityName);

    [McpServerTool(Name = "lookup_bigworld_api")]
    [Description("Look up BigWorld engine API usage. Without api_name: lists all BigWorld APIs used. With api_name: shows which methods call this API and any C++ reimplementations.")]
    public Task<string> LookupBigWorldApi(
        [Description("BigWorld API name e.g. time, addTimer, createEntity")] string? apiName = null)
        => _graph.LookupBigWorldApiAsync(apiName);
}
