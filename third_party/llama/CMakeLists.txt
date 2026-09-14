cmake_minimum_required(VERSION 3.25...4.4)

project(
    umsatzschaetzung_llm
    DESCRIPTION "llama.cpp behind the three-function Umsatzschätzung C ABI"
    LANGUAGES C CXX
    VERSION 1.0.0
)

find_package(llama REQUIRED)
find_package(Vulkan REQUIRED)

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
