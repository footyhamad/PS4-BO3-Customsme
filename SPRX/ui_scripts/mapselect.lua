if rawget(_G, "BO3CustomsMapTabs") ~= nil then
  return
end

local Tabs = {
  current = {},
  menuWrapped = false,
}
rawset(_G, "BO3CustomsMapTabs", Tabs)

local TabOrder = {
  { id = "standard", label = "STANDARD" },
  { id = "custom", label = "CUSTOM" },
  { id = "modloader", label = "MOD LOADER" },
}

local CustomsCategory = 10100

local function Zombies()
  return Enum ~= nil and Enum.eModes ~= nil and Enum.eModes.MODE_ZOMBIES or 0
end

local function Multiplayer()
  return Enum ~= nil and Enum.eModes ~= nil and Enum.eModes.MODE_MULTIPLAYER or 1
end

local function AllCustom()
  local list = rawget(_G, "BO3CustomMaps")
  if type(list) ~= "table" then
    return {}
  end
  return list
end

local function ModeOf(map)
  if map.mode == "zm" or map.mode == "mp" then
    return map.mode
  end
  if type(map.id) == "string" and string.sub(map.id, 1, 3) == "mp_" then
    return "mp"
  end
  return "zm"
end

local function CustomList(mode)
  local only = {}
  for _, map in ipairs(AllCustom()) do
    if type(map) == "table" and ModeOf(map) == mode then
      table.insert(only, map)
    end
  end
  return only
end

local function IsCustom(info)
  return type(info) == "table" and info.bo3_custom == true
end

local function Template(maps, mode)
  local preferred = mode == "mp" and maps.mp_sector or maps.zm_zod
  if type(preferred) == "table" then
    return preferred
  end
  local sessionMode = mode == "mp" and Multiplayer() or Zombies()
  for _, info in pairs(maps) do
    if type(info) == "table" and info.session_mode == sessionMode and not IsCustom(info) and info.isFreeRunMap ~= true then
      return info
    end
  end
  return nil
end

local function EnsureCustomEntries()
  local maps = CoD.mapsTable
  if type(maps) ~= "table" then
    return false
  end
  local templates = {}
  for index, map in ipairs(AllCustom()) do
    if type(map) == "table" and type(map.id) == "string" and maps[map.id] == nil then
      local mode = ModeOf(map)
      if templates[mode] == nil then
        templates[mode] = Template(maps, mode) or false
      end
      local template = templates[mode]
      if template then
        local info = {}
        for key, value in pairs(template) do
          info[key] = value
        end
        local title = map.name or map.id
        info.name = title
        info.mapName = title
        info.mapNameCaps = string.upper(title)
        info.mapDescription = map.desc or ""
        info.mapLocation = "CUSTOM MAP"
        info.previewImage = map.preview or map.loading or "$white"
        info.loadingImage = map.loading or map.preview or "$white"
        info.introMovie = map.movie
        info.dlc_pack = 0
        info.unique_id = 100000 + index
        info.bo3_custom = true
        maps[map.id] = info
      end
    end
  end
  return true
end

local function TabOf(info)
  if IsCustom(info) then
    return "custom"
  end
  return "standard"
end

local function TabTable(maps, tab)
  local only = {}
  for id, info in pairs(maps) do
    if type(info) == "table" and info.session_mode == Zombies() and TabOf(info) == tab then
      only[id] = info
    end
  end
  return only
end

local function Current(controller)
  return Tabs.current[controller or 0] or "standard"
end

local function StartingTab(controller)
  local mapName = Engine.DvarString(nil, "ui_mapname")
  local info = type(CoD.mapsTable) == "table" and CoD.mapsTable[mapName] or nil
  if type(info) == "table" and info.session_mode == Zombies() then
    return TabOf(info)
  end
  return Current(controller)
end

DataSources.BO3CustomsMapTabs = DataSourceHelpers.ListSetup("BO3CustomsMapTabs", function(controller)
  local list = {}
  local current = Current(controller)
  table.insert(list, {
    models = { tabIcon = CoD.buttonStrings.shoulderl },
    properties = { m_mouseDisabled = true },
  })
  for _, tab in ipairs(TabOrder) do
    table.insert(list, {
      models = { tabName = tab.label, tabIcon = "", bo3Tab = tab.id },
      properties = { tabId = tab.id, selectIndex = tab.id == current },
    })
  end
  table.insert(list, {
    models = { tabIcon = CoD.buttonStrings.shoulderr },
    properties = { m_mouseDisabled = true },
  })
  return list
end, true)

local function Choosing(controller)
  local perController = CoD.perController ~= nil and CoD.perController[controller] or nil
  return perController ~= nil and perController.choosingZMPlaylist == true
end

local function BuildWith(maps, prepare, ...)
  local full = CoD.mapsTable
  CoD.mapsTable = maps
  local ok, a, b, c = pcall(prepare, ...)
  CoD.mapsTable = full
  if not ok then
    error(a)
  end
  return a, b, c
end

local function WrapMapsList()
  local source = DataSources.ZMMapsList
  if type(source) ~= "table" or type(source.prepare) ~= "function" or source.bo3Wrapped then
    return false
  end
  local prepare = source.prepare
  source.bo3Wrapped = true
  source.prepare = function(controller, ...)
    if type(controller) ~= "number" or Choosing(controller) or type(CoD.mapsTable) ~= "table" then
      return prepare(controller, ...)
    end
    EnsureCustomEntries()
    return BuildWith(TabTable(CoD.mapsTable, Current(controller)), prepare, controller, ...)
  end
  return true
end

local function WrapCombatRecord()
  local source = DataSources.CombatRecordZMMapsList
  if type(source) ~= "table" or type(source.prepare) ~= "function" or source.bo3Wrapped then
    return
  end
  local prepare = source.prepare
  source.bo3Wrapped = true
  source.prepare = function(...)
    if type(CoD.mapsTable) ~= "table" then
      return prepare(...)
    end
    local stock = {}
    for id, info in pairs(CoD.mapsTable) do
      if not IsCustom(info) then
        stock[id] = info
      end
    end
    return BuildWith(stock, prepare, ...)
  end
end

local function AddTabs(menu, controller)
  if menu.BO3TabBar ~= nil or menu.MapList == nil or Choosing(controller) then
    return
  end
  if CoD.FE_TabBar == nil then
    pcall(require, "ui.uieditor.widgets.Lobby.Common.FE_TabBar")
  end
  if CoD.FE_TabBar == nil then
    return
  end
  EnsureCustomEntries()
  Tabs.current[controller] = StartingTab(controller)

  local bar = CoD.FE_TabBar.new(menu, controller)
  bar:setLeftRight(true, false, 0, 2497)
  bar:setTopBottom(true, false, 92, 133)
  bar.Tabs.grid:setHorizontalCount(5)
  bar.Tabs.grid:setDataSource("BO3CustomsMapTabs")
  menu:addElement(bar)
  menu.BO3TabBar = bar

  menu.MapList:setTopBottom(true, false, 142, 548)

  local watcher = LUI.UIElement.new()
  menu:addElement(watcher)
  watcher:linkToElementModel(bar.Tabs.grid, "bo3Tab", true, function(model)
    local tab = Engine.GetModelValue(model)
    if type(tab) ~= "string" or tab == "" or tab == Current(controller) then
      return
    end
    Tabs.current[controller] = tab
    menu.MapList:updateDataSource()
  end)

  menu.MapList:updateDataSource()
end

local function WrapMenu()
  if Tabs.menuWrapped or LUI.createMenu == nil or type(LUI.createMenu.ZMMapSelection) ~= "function" then
    return false
  end
  local create = LUI.createMenu.ZMMapSelection
  Tabs.menuWrapped = true
  LUI.createMenu.ZMMapSelection = function(controller, ...)
    local menu = create(controller, ...)
    if menu ~= nil then
      pcall(AddTabs, menu, controller)
    end
    return menu
  end
  return true
end

local function AddCustomsCategory(items)
  local maps = CoD.mapsTable
  if type(items) ~= "table" or type(maps) ~= "table" or CoD.isCampaign == true then
    return
  end
  EnsureCustomEntries()
  local count = 0
  for _, info in pairs(maps) do
    if IsCustom(info) and info.session_mode == CoD.gameModeEnum then
      count = count + 1
    end
  end
  if count == 0 then
    return
  end
  local current = maps[Engine.DvarString(nil, "ui_mapname")]
  local onCustom = IsCustom(current) and current.session_mode == CoD.gameModeEnum
  if onCustom then
    for _, item in ipairs(items) do
      if type(item) == "table" and type(item.properties) == "table" then
        item.properties.selectIndex = false
      end
    end
  end
  table.insert(items, {
    models = {
      text = "CUSTOMS",
      buttonText = "CUSTOMS",
      image = "playlist_map",
      description = "Choose from the custom maps installed on this console.",
    },
    properties = { category = CustomsCategory, selectIndex = onCustom },
  })
end

local function PrepareCategories(prepare, controller, list)
  local items = prepare(controller, list)
  pcall(AddCustomsCategory, items)
  return items
end

local function PrepareMaps(prepare, controller, list)
  local maps = CoD.mapsTable
  local perController = CoD.perController ~= nil and CoD.perController[controller] or nil
  if type(maps) ~= "table" or type(perController) ~= "table" then
    return prepare(controller, list)
  end
  EnsureCustomEntries()
  local customs = perController.mapCategory == CustomsCategory
  local only = {}
  for id, info in pairs(maps) do
    if IsCustom(info) == customs then
      only[id] = info
    end
  end
  CoD.mapsTable = only
  if customs then
    perController.mapCategory = 0
  end
  local ok, result = pcall(prepare, controller, list)
  CoD.mapsTable = maps
  if customs then
    perController.mapCategory = CustomsCategory
  end
  if not ok then
    error(result)
  end
  return result
end

local ListPrepares = {
  ChangeMapCategories = PrepareCategories,
  ChangeMapMaps = PrepareMaps,
}

local function WrapListPrepare()
  if Tabs.listPrepareWrapped then
    return true
  end
  local prepareList = rawget(_G, "ListHelper_Prepare")
  if type(prepareList) ~= "function" then
    return false
  end
  Tabs.listPrepareWrapped = true
  rawset(_G, "ListHelper_Prepare", function(list, controller, name, prepare, ...)
    local wrapper = name ~= nil and ListPrepares[name] or nil
    if wrapper ~= nil and type(prepare) == "function" then
      local stock = prepare
      prepare = function(prepareController, listElement)
        return wrapper(stock, prepareController, listElement)
      end
    end
    return prepareList(list, controller, name, prepare, ...)
  end)
  return true
end

local function Install()
  WrapListPrepare()
  WrapMapsList()
  WrapCombatRecord()
  WrapMenu()
  EnsureCustomEntries()
  return Tabs.listPrepareWrapped == true and Tabs.menuWrapped and type(DataSources.ZMMapsList) == "table" and
    DataSources.ZMMapsList.bo3Wrapped == true
end

if not Install() then
  local tick = rawget(_G, "BO3CustomsTick")
  rawset(_G, "BO3CustomsTick", function()
    if not Tabs.installed and Install() then
      Tabs.installed = true
    end
    if tick ~= nil then
      tick()
    end
  end)
end
