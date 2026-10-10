local maps = ...
if type(maps) ~= "table" or type(BO3CustomMaps) ~= "table" then
  return
end
local function Template(prefix, preferred)
  if type(maps[preferred]) == "table" then
    return maps[preferred]
  end
  for id, info in pairs(maps) do
    if type(id) == "string" and string.sub(id, 1, 3) == prefix and type(info) == "table" and info.isFreeRunMap ~= true and
      info.bo3_custom ~= true then
      return info
    end
  end
  return nil
end
local templates = { zm = Template("zm_", "zm_zod"), mp = Template("mp_", "mp_sector") }
for index, map in ipairs(BO3CustomMaps) do
  local template = templates[map.mode or "zm"]
  if type(map.id) == "string" and type(template) == "table" and maps[map.id] == nil then
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
