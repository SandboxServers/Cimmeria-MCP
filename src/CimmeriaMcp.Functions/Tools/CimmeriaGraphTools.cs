using CimmeriaMcp.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;

namespace CimmeriaMcp.Functions.Tools;

public class CimmeriaGraphTools
{
    private readonly CimmeriaGraphService _graphService;
    private readonly SignalRBroadcastService _signalR;

    public CimmeriaGraphTools(CimmeriaGraphService graphService, SignalRBroadcastService signalR)
    {
        _graphService = graphService;
        _signalR = signalR;
    }

    [Function(nameof(GetEntityDetails))]
    public async Task<string> GetEntityDetails(
        [McpToolTrigger("get_entity_details",
            "Get full details about an SGW entity or interface from the knowledge graph — properties, methods, inheritance, interfaces, and game system links. Entity names: Account, SGWBeing, SGWBlackMarket, SGWChannelManager, SGWCoverSet, SGWDuelMarker, SGWEntity, SGWEscrow, SGWGmPlayer, SGWMob, SGWPet, SGWPlayer, SGWPlayerGroupAuthority, SGWPlayerRespawner, SGWSpaceCreator, SGWSpawnRegion, SGWSpawnSet, SGWSpawnableEntity. Interface names: ClientCache, Communicator, ContactListManager, DistributionGroupMember, EventParticipant, GateTravel, GroupAuthority, Lootable, MinigamePlayer, Missionary, OrganizationMember, SGWAbilityManager, SGWBeing, SGWBlackMarketManager, SGWCombatant, SGWInventoryManager, SGWMailManager, SGWPoller.")]
        ToolInvocationContext context,
        [McpToolProperty("entity_name", "Entity or interface name e.g. SGWPlayer, SGWCombatant", isRequired: true)] string entityName)
    {
        return await _signalR.TrackToolAsync("get_entity_details",
            () => _graphService.GetEntityDetailsAsync(entityName));
    }

    [Function(nameof(GetInheritanceTree))]
    public async Task<string> GetInheritanceTree(
        [McpToolTrigger("get_inheritance_tree",
            "Get the full inheritance hierarchy for an entity — ancestors, descendants, and implemented interfaces.")]
        ToolInvocationContext context,
        [McpToolProperty("entity_name", "Entity name e.g. SGWPlayer", isRequired: true)] string entityName)
    {
        return await _signalR.TrackToolAsync("get_inheritance_tree",
            () => _graphService.GetInheritanceTreeAsync(entityName));
    }

    [Function(nameof(GetGraphOverview))]
    public async Task<string> GetGraphOverview(
        [McpToolTrigger("get_graph_overview",
            "Get an overview of the SGW knowledge graph — vertex/edge counts, all entities, interfaces, and game systems.")]
        ToolInvocationContext context)
    {
        return await _signalR.TrackToolAsync("get_graph_overview",
            () => _graphService.GetGraphOverviewAsync());
    }

    [Function(nameof(GetGameSystemDetails))]
    public async Task<string> GetGameSystemDetails(
        [McpToolTrigger("get_game_system_details",
            "Get details about a specific game system and its associated entities. Systems: combat, inventory, missions, chat, spawning, gate_travel, organizations, trading, groups, account, minigame, cover.")]
        ToolInvocationContext context,
        [McpToolProperty("system_name", "Game system name e.g. combat, inventory, gate_travel", isRequired: true)] string systemName)
    {
        return await _signalR.TrackToolAsync("get_game_system_details",
            () => _graphService.GetGameSystemDetailsAsync(systemName));
    }

    [Function(nameof(GetReplicatedProperties))]
    public async Task<string> GetReplicatedProperties(
        [McpToolTrigger("get_replicated_properties",
            "Get all client-visible (CELL_PUBLIC) properties for an entity, including inherited properties from the full inheritance chain and implemented interfaces. Essential for understanding what data the client sees.")]
        ToolInvocationContext context,
        [McpToolProperty("entity_name", "Entity name e.g. SGWPlayer, SGWMob", isRequired: true)] string entityName)
    {
        return await _signalR.TrackToolAsync("get_replicated_properties",
            () => _graphService.GetReplicatedPropertiesAsync(entityName));
    }

    [Function(nameof(GetMethodCallChain))]
    public async Task<string> GetMethodCallChain(
        [McpToolTrigger("get_method_call_chain",
            "Trace method call chains — what a specific method calls (client RPC, base/cell messages, BigWorld APIs) and what calls it. Useful for understanding data flow and message passing.")]
        ToolInvocationContext context,
        [McpToolProperty("entity_name", "Entity name e.g. SGWMob", isRequired: true)] string entityName,
        [McpToolProperty("method_name", "Method name e.g. threatGenerated, doAiAction", isRequired: true)] string methodName)
    {
        return await _signalR.TrackToolAsync("get_method_call_chain",
            () => _graphService.GetMethodCallChainAsync(entityName, methodName));
    }

    [Function(nameof(TraverseGraph))]
    public async Task<string> TraverseGraph(
        [McpToolTrigger("traverse_graph",
            "Traverse the knowledge graph from a starting entity following a specific edge type. Edge types: inherits, implements, has_property, has_client_method, has_cell_method, has_base_method, part_of_system, defined_in, implements_def, python_inherits, sends_to_client, sends_to_base, calls_self.")]
        ToolInvocationContext context,
        [McpToolProperty("start_entity", "Starting entity name", isRequired: true)] string startEntity,
        [McpToolProperty("edge_type", "Edge type to follow e.g. inherits, implements, sends_to_client", isRequired: true)] string edgeType,
        [McpToolProperty("depth", "Max traversal depth (1-5, default 2)")] int? depth)
    {
        var d = Math.Clamp(depth ?? 2, 1, 5);
        return await _signalR.TrackToolAsync("traverse_graph",
            () => _graphService.TraverseGraphAsync(startEntity, edgeType, d));
    }

    // ====================================================================
    // Enhancement #1: Enum & Type Resolver
    // ====================================================================

    [Function(nameof(LookupEnum))]
    public async Task<string> LookupEnum(
        [McpToolTrigger("lookup_enum",
            "Look up SGW enumerations and constants. Search by enum name (e.g. EAbilityFlags, EDamageType) to get all tokens/values, or search by token name to find which enum it belongs to. Also searches Atrea.enums constants. 124 enumerations + 1276 constants available.")]
        ToolInvocationContext context,
        [McpToolProperty("enum_name", "Enumeration name e.g. EAbilityFlags, EDamageType, EArchetype")] string? enumName,
        [McpToolProperty("token_name", "Search for a token/constant by name e.g. ARCHETYPE_Soldier, GENDER_Male")] string? tokenName)
    {
        return await _signalR.TrackToolAsync("lookup_enum",
            () => _graphService.LookupEnumAsync(enumName ?? "", tokenName));
    }

    [Function(nameof(ResolveType))]
    public async Task<string> ResolveType(
        [McpToolTrigger("resolve_type",
            "Resolve a BigWorld type alias to its full structure. Handles simple aliases (CONTROLLER_ID → INT32), arrays (CharacterInfoList → ARRAY<CharacterInfo>), and FIXED_DICT compound types with all fields (e.g. CharacterInfo, StatList, SlotType, InvItem, MissionStatus). 63 type aliases available.")]
        ToolInvocationContext context,
        [McpToolProperty("type_name", "Type name e.g. CharacterInfo, StatList, LootItemQuantity", isRequired: true)] string typeName)
    {
        return await _signalR.TrackToolAsync("resolve_type",
            () => _graphService.ResolveTypeAsync(typeName));
    }

    // ====================================================================
    // Enhancement #2: Game Data Definition Lookup
    // ====================================================================

    [Function(nameof(LookupGameDef))]
    public async Task<string> LookupGameDef(
        [McpToolTrigger("lookup_game_def",
            "Look up a game data definition schema — fields, cross-references to other defs, and enum references. Definitions: Ability, Effect, Item, Mission, LootTable, Dialog, Sequence, Stargate, EntityTemplate, Container, Discipline, Blueprint, AbilitySet, InteractionSet, EventSet, WorldInfo, and more. Shows what data each game object contains.")]
        ToolInvocationContext context,
        [McpToolProperty("def_name", "Definition class name e.g. Ability, Item, Mission, LootTable", isRequired: true)] string defName)
    {
        return await _signalR.TrackToolAsync("lookup_game_def",
            () => _graphService.LookupGameDefAsync(defName));
    }

    // ====================================================================
    // Enhancement #3: Implementation Coverage
    // ====================================================================

    [Function(nameof(GetImplementationStatus))]
    public async Task<string> GetImplementationStatus(
        [McpToolTrigger("get_implementation_status",
            "Analyze implementation coverage — compare .def method definitions against Python and C++ implementations. Without entity_name: shows overview of all entities and C++ components. With entity_name: shows per-method coverage (defined vs Python implemented vs C++ implemented).")]
        ToolInvocationContext context,
        [McpToolProperty("entity_name", "Entity name for per-method analysis, or omit for overview")] string? entityName)
    {
        return await _signalR.TrackToolAsync("get_implementation_status",
            () => _graphService.GetImplementationStatusAsync(entityName));
    }

    // ====================================================================
    // Enhancement #4: Cross-Reference Search
    // ====================================================================

    [Function(nameof(CrossReference))]
    public async Task<string> CrossReference(
        [McpToolTrigger("cross_reference",
            "Search across the entire knowledge graph for a term — finds matching methods (def/Python/C++), properties, enumerations, constants, game definitions, and call chains. One query, full picture across all codebases.")]
        ToolInvocationContext context,
        [McpToolProperty("search_term", "Term to search for e.g. threatGenerated, Alignment, ARCHETYPE", isRequired: true)] string searchTerm)
    {
        return await _signalR.TrackToolAsync("cross_reference",
            () => _graphService.CrossReferenceAsync(searchTerm));
    }

    // ====================================================================
    // Enhancement #5: Protocol Message Map
    // ====================================================================

    [Function(nameof(GetEntityProtocol))]
    public async Task<string> GetEntityProtocol(
        [McpToolTrigger("get_entity_protocol",
            "Get the complete client-server protocol for an entity — all client methods (server→client), exposed base methods (client→server), cell methods (gameplay logic), and auto-replicated properties (CELL_PUBLIC). Includes inherited methods from the full chain. This is the API contract needed for client compatibility.")]
        ToolInvocationContext context,
        [McpToolProperty("entity_name", "Entity name e.g. SGWPlayer, Account, SGWMob", isRequired: true)] string entityName)
    {
        return await _signalR.TrackToolAsync("get_entity_protocol",
            () => _graphService.GetEntityProtocolAsync(entityName));
    }

    // ====================================================================
    // Enhancement #6: BigWorld API Reference
    // ====================================================================

    [Function(nameof(LookupBigWorldApi))]
    public async Task<string> LookupBigWorldApi(
        [McpToolTrigger("lookup_bigworld_api",
            "Look up BigWorld engine API usage. Without api_name: lists all BigWorld APIs used across the Python codebase with usage counts. With api_name: shows which methods call this API and any C++ reimplementations. APIs include BigWorld.time(), BigWorld.addTimer(), BigWorld.createEntity(), etc.")]
        ToolInvocationContext context,
        [McpToolProperty("api_name", "BigWorld API name e.g. time, addTimer, createEntity")] string? apiName)
    {
        return await _signalR.TrackToolAsync("lookup_bigworld_api",
            () => _graphService.LookupBigWorldApiAsync(apiName));
    }
}
