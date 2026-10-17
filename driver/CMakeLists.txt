cmake_minimum_required(VERSION 3.16)
project(libss551 VERSION 0.1.0 LANGUAGES C)

set(CMAKE_C_STANDARD 11)
add_compile_definitions(_GNU_SOURCE)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_VISIBILITY_PRESET hidden)

find_package(PkgConfig REQUIRED)
pkg_check_modules(HIDAPI REQUIRED IMPORTED_TARGET hidapi-hidraw)

add_library(ss551 SHARED src/ss551.c src/vkbd.c)
target_include_directories(ss551 PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include)
target_link_libraries(ss551 PRIVATE PkgConfig::HIDAPI)
target_compile_options(ss551 PRIVATE -Wall -Wextra)
set_target_properties(ss551 PROPERTIES VERSION ${PROJECT_VERSION} SOVERSION 0)

add_executable(ss551-probe tools/ss551-probe.c)
target_link_libraries(ss551-probe PRIVATE ss551 PkgConfig::HIDAPI)
target_compile_options(ss551-probe PRIVATE -Wall -Wextra)

include(GNUInstallDirs)
install(TARGETS ss551 ss551-probe)
install(FILES include/ss551.h DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
