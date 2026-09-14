cmake_minimum_required(VERSION 3.25...4.4)

project(
    umsatzschaetzung_llm
    DESCRIPTION "llama.cpp behind the three-function Umsatzschätzung C ABI"
    LANGUAGES C CXX
    VERSION 1.0.0
)

find_package(llama REQUIRED)
find_package(Vulkan REQUIRED)

# FindVulkan picks vulkan-1.lib by path alone, and lld-link accepts an import
# library of the wrong machine type by copying its thunks verbatim: an x64 SDK
# on an ARM64 target yields a dll that dies on the first Vulkan call. The short
# import members of the archive carry the machine word, so check it here.
if(WIN32)
    if(CMAKE_SYSTEM_PROCESSOR MATCHES "^(ARM64|aarch64)$")
        set(expected_machine 64aa)
    else()
        set(expected_machine 6486)
    endif()
    file(READ "${Vulkan_LIBRARY}" vulkan_lib_hex HEX)
    string(REGEX MATCH "600a0000ffff[0-9a-f][0-9a-f][0-9a-f][0-9a-f]([0-9a-f][0-9a-f][0-9a-f][0-9a-f])" _match "${vulkan_lib_hex}")
    if(NOT _match)
        message(FATAL_ERROR "cannot read machine type of ${Vulkan_LIBRARY}")
    elseif(NOT CMAKE_MATCH_1 STREQUAL expected_machine)
        message(FATAL_ERROR "${Vulkan_LIBRARY} is built for a different architecture than ${CMAKE_SYSTEM_PROCESSOR}: install the matching Vulkan SDK")
    endif()
endif()

add_library(umsatzschaetzung_llm SHARED)
target_sources(
    umsatzschaetzung_llm
    PRIVATE engine.c
    PUBLIC FILE_SET HEADERS BASE_DIRS "${CMAKE_CURRENT_SOURCE_DIR}" FILES engine.h
)
target_compile_features(umsatzschaetzung_llm PRIVATE c_std_11)
target_link_libraries(umsatzschaetzung_llm PRIVATE llama Vulkan::Vulkan)
set_target_properties(
    umsatzschaetzung_llm
    PROPERTIES
        PREFIX ""
        C_VISIBILITY_PRESET hidden
        CXX_VISIBILITY_PRESET hidden
        LINKER_LANGUAGE CXX
        POSITION_INDEPENDENT_CODE ON
)

include(GNUInstallDirs)
install(TARGETS umsatzschaetzung_llm EXPORT umsatzschaetzung_llm FILE_SET HEADERS)
install(EXPORT umsatzschaetzung_llm DESTINATION "${CMAKE_INSTALL_LIBDIR}/cmake/umsatzschaetzung_llm" FILE umsatzschaetzung_llm-config.cmake)
