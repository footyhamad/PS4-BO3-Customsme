if rawget(_G, "BO3CustomsGraphics") ~= nil then
  return
end

local prepareList = rawget(_G, "ListHelper_Prepare")
local cod = rawget(_G, "CoD")

if type(prepareList) ~= "function" or type(cod) ~= "table" or type(rawget(cod, "OptionsUtility")) ~= "table" then
  return
end

rawset(_G, "BO3CustomsGraphics", {})

local function Current(dvarName)
  local ok, value = pcall(Engine.DvarInt, nil, dvarName)
  if ok and type(value) == "number" then
    return value
  end
  return nil
end

local function Choose(row, item, controller, dvarName)
  if item == nil or type(dvarName) ~= "string" then
    return
  end
  local value = item.value
  if type(value) ~= "number" or value == Current(dvarName) then
    return
  end
  Engine.SetDvar(dvarName, value)
  Engine.SetDvar("bo3customs_graphics_save", 1)
end

local function OnOffLabel(value)
  if value == 0 then
    return "MENU_DISABLED"
  elseif value == 1 then
    return "MENU_ENABLED"
  end
  return tostring(value)
end

local function FpsLabel(value)
  if value == 0 then
    return "Unlimited"
  end
  return tostring(value)
end

local function Options(dvarName, values, default, label)
  local ordered = {}
  local current = Current(dvarName)
  local known = current == nil
  for _, value in ipairs(values) do
    table.insert(ordered, value)
    if value == current then
      known = true
    end
  end
  if not known then
    local at = #ordered + 1
    for index, value in ipairs(ordered) do
      if value ~= 0 and value > current then
        at = index
        break
      end
    end
    if at > #ordered and ordered[#ordered] == 0 and current ~= 0 then
      at = #ordered
    end
    table.insert(ordered, at, current)
  end
  local options = {}
  for _, value in ipairs(ordered) do
    table.insert(options, { option = label(value), value = value, default = value == default })
  end
  return options
end

local function Row(controller, title, desc, id, dvarName, values, default, label)
  return CoD.OptionsUtility.CreateDvarSettings(controller, title, desc, "BO3CustomsGraphics_" .. id, dvarName,
    Options(dvarName, values, default, label), nil, Choose)
end

local function Rows(controller)
  return {
    Row(controller, "V-Sync", "Waits for the display before showing each frame. Turn it off to go past 60 FPS; the picture can tear.",
      "vsync", "r_vsync", { 0, 1 }, 1, OnOffLabel),
    Row(controller, "Max FPS", "The highest frame rate the game runs at. Anything above 60 needs V-Sync off.",
      "maxfps", "com_maxfps", { 30, 45, 60, 75, 90, 100, 120, 144, 165, 200, 240, 0 }, 60, FpsLabel),
    Row(controller, "Show FPS", "Shows the frame rate counter on screen.",
      "drawfps", "cg_drawFPS", { 0, 1 }, 0, OnOffLabel),
    Row(controller, "Field of View", "How wide the view is. 65 is the console default.",
      "fov", "cg_fov", { 65, 70, 75, 80, 85, 90, 95, 100, 105, 110, 115, 120 }, 65, tostring),
  }
end

rawset(_G, "ListHelper_Prepare", function(list, controller, name, build, ...)
  if name == "OptionGraphicsList" and type(build) == "function" then
    local stock = build
    build = function(listController, element)
      local rows = stock(listController, element)
      if type(rows) ~= "table" then
        return rows
      end
      local ok, extra = pcall(Rows, listController)
      if ok and type(extra) == "table" then
        for _, row in ipairs(extra) do
          table.insert(rows, row)
        end
      end
      return rows
    end
  end
  return prepareList(list, controller, name, build, ...)
end)
