# OpenOrbis build for PS4 BO3 Customs SPRX.
# Usage: export OO_PS4_TOOLCHAIN=/path/to/OpenOrbis-PS4-Toolchain && make -C SPRX
ifndef OO_PS4_TOOLCHAIN
$(error OO_PS4_TOOLCHAIN is not set; point it at the extracted OpenOrbis PS4 toolchain root)
endif

CLANG   ?= clang-18
CLANGXX ?= clang++-18
LLD     ?= ld.lld-18
OBJCOPY ?= objcopy
NM      ?= nm
READELF ?= readelf
TOOLCHAIN := $(OO_PS4_TOOLCHAIN)
BUILD_DIR ?= build/openorbis
TARGET := $(BUILD_DIR)/BO3-Customs.sprx
ELF := $(BUILD_DIR)/BO3-Customs.elf
OELF := $(BUILD_DIR)/BO3-Customs.oelf
CRT_LOCAL := $(BUILD_DIR)/crtlib-local.o
LINKER_SCRIPT := $(BUILD_DIR)/link.x
SYMBOL_REPORT := $(BUILD_DIR)/BO3-Customs.symbols.txt

LIBS := -lc -lc++ -lkernel -lSceSysmodule -lSceUserService -lSceMouse

COMMON_FLAGS := --target=x86_64-pc-freebsd12-elf -fPIC -funwind-tables \
	-DBO3_OPENORBIS=1 -Iopenorbis_compat \
	-isysroot $(TOOLCHAIN) -isystem $(TOOLCHAIN)/include
CFLAGS := $(COMMON_FLAGS) -c
CXXFLAGS := $(COMMON_FLAGS) -isystem $(TOOLCHAIN)/include/c++/v1 -std=c++20 -fno-exceptions -c
LDFLAGS := -m elf_x86_64 -pie --script $(LINKER_SCRIPT) --eh-frame-hdr \
	-L$(TOOLCHAIN)/lib $(LIBS)

C_SOURCES := HDE64.c
CPP_SOURCES := $(filter-out libjbc.cpp,$(wildcard *.cpp))
OBJECTS := $(patsubst %.c,$(BUILD_DIR)/%.o,$(C_SOURCES)) \
	$(patsubst %.cpp,$(BUILD_DIR)/%.o,$(CPP_SOURCES))

.PHONY: all clean check-tools
all: check-tools $(TARGET)

check-tools:
	@test -f "$(TOOLCHAIN)/link.x" || (echo "ERROR: $(TOOLCHAIN)/link.x not found; OO_PS4_TOOLCHAIN must be the SDK root" >&2; exit 1)
	@test -f "$(TOOLCHAIN)/lib/crtlib.o" || (echo "ERROR: $(TOOLCHAIN)/lib/crtlib.o not found in OpenOrbis SDK" >&2; exit 1)
	@command -v "$(CLANGXX)" >/dev/null 2>&1 || (echo "ERROR: $(CLANGXX) not found" >&2; exit 1)
	@command -v "$(LLD)" >/dev/null 2>&1 || (echo "ERROR: $(LLD) not found" >&2; exit 1)
	@command -v "$(OBJCOPY)" >/dev/null 2>&1 || (echo "ERROR: $(OBJCOPY) not found" >&2; exit 1)
	@command -v "$(NM)" >/dev/null 2>&1 || (echo "ERROR: $(NM) not found" >&2; exit 1)
	@command -v "$(READELF)" >/dev/null 2>&1 || (echo "ERROR: $(READELF) not found" >&2; exit 1)
	@command -v python3 >/dev/null 2>&1 || (echo "ERROR: python3 not found" >&2; exit 1)
	@test -x "$(TOOLCHAIN)/bin/linux/create-fself" || (echo "ERROR: create-fself is missing from the SDK release" >&2; exit 1)

$(BUILD_DIR):
	mkdir -p "$@"

$(LINKER_SCRIPT): $(TOOLCHAIN)/link.x prepare_openorbis_linker.py | $(BUILD_DIR)
	python3 prepare_openorbis_linker.py "$<" "$@"

$(CRT_LOCAL): $(TOOLCHAIN)/lib/crtlib.o | $(BUILD_DIR)
	$(OBJCOPY) --localize-symbol=module_start --localize-symbol=module_stop "$<" "$@"
	@echo "Localized only OpenOrbis CRT fallback entrypoints in $@"

$(BUILD_DIR)/%.o: %.cpp | $(BUILD_DIR)
	$(CLANGXX) $(CXXFLAGS) -o "$@" "$<"

$(BUILD_DIR)/%.o: %.c | $(BUILD_DIR)
	$(CLANG) $(CFLAGS) -o "$@" "$<"

$(ELF): $(OBJECTS) $(CRT_LOCAL) $(LINKER_SCRIPT)
	$(LLD) $(OBJECTS) -o "$@" $(LDFLAGS) "$(CRT_LOCAL)"
	python3 verify_openorbis_elf.py --nm "$(NM)" --readelf "$(READELF)" "$@" | tee "$(SYMBOL_REPORT)"

$(TARGET): $(ELF)
	"$(TOOLCHAIN)/bin/linux/create-fself" -in="$(ELF)" -out="$(OELF)" --lib="$(TARGET)" --paid=0x3800000000000011 --fwversion=0x04050000
	@test -s "$@" || (echo "ERROR: create-fself did not produce $(TARGET)" >&2; exit 1)
	@file "$@"
	@sha256sum "$@" | tee "$(TARGET).sha256"

clean:
	rm -f "$(BUILD_DIR)"/*.o "$(ELF)" "$(OELF)" "$(TARGET)" "$(TARGET).sha256" "$(LINKER_SCRIPT)" "$(SYMBOL_REPORT)"
