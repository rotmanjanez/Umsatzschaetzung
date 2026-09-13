cmake_minimum_required(VERSION 3.25...4.4)

project(
    ausbeute_llm
    DESCRIPTION "llama.cpp behind the three-function Ausbeute C ABI"
    LANGUAGES C CXX
    VERSION 1.0.0
)

find_package(llama REQUIRED)

add_library(ausbeute_llm SHARED)
target_sources(
    ausbeute_llm
    PRIVATE engine.c
    PUBLIC FILE_SET HEADERS BASE_DIRS "${CMAKE_CURRENT_SOURCE_DIR}" FILES engine.h
)
target_compile_features(ausbeute_llm PRIVATE c_std_11)
target_link_libraries(ausbeute_llm PRIVATE llama)
set_target_properties(
    ausbeute_llm
    PROPERTIES
        PREFIX ""
        C_VISIBILITY_PRESET hidden
        CXX_VISIBILITY_PRESET hidden
        LINKER_LANGUAGE CXX
        POSITION_INDEPENDENT_CODE ON
)

include(GNUInstallDirs)
install(TARGETS ausbeute_llm EXPORT ausbeute_llm FILE_SET HEADERS)
install(EXPORT ausbeute_llm DESTINATION "${CMAKE_INSTALL_LIBDIR}/cmake/ausbeute_llm" FILE ausbeute_llm-config.cmake)
