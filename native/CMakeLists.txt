cmake_minimum_required(VERSION 3.16)
project(orderednauty C)

if(NOT DEFINED NAUTY_DIR)
  message(FATAL_ERROR "Add -DNAUTY_DIR=/path/to/nauty2_8_9")
endif()

set(NAUTY_SOURCES
  ${NAUTY_DIR}/nauty.c ${NAUTY_DIR}/nautil.c ${NAUTY_DIR}/nausparse.c
  ${NAUTY_DIR}/naugraph.c ${NAUTY_DIR}/schreier.c ${NAUTY_DIR}/naurng.c
  ${NAUTY_DIR}/traces.c ${NAUTY_DIR}/gtools.c ${NAUTY_DIR}/naututil.c
  ${NAUTY_DIR}/nautinv.c ${NAUTY_DIR}/gutil1.c ${NAUTY_DIR}/gutil2.c
  ${NAUTY_DIR}/gtnauty.c ${NAUTY_DIR}/naugroup.c ${NAUTY_DIR}/nautycliquer.c
  ${NAUTY_DIR}/nauchromatic.c ${NAUTY_DIR}/nauconnect.c
)
foreach(SRC IN LISTS NAUTY_SOURCES)
  if(NOT EXISTS "${SRC}")
    message(FATAL_ERROR "Missing nauty source: ${SRC}")
  endif()
endforeach()

add_library(orderednauty SHARED
  ordered_nauty_bridge.c
  ${NAUTY_SOURCES}
)

target_include_directories(orderednauty PRIVATE "${NAUTY_DIR}")
set_target_properties(orderednauty PROPERTIES C_STANDARD 11 C_STANDARD_REQUIRED YES)
target_compile_options(orderednauty PRIVATE -O3)
if(NOT WIN32)
  target_compile_options(orderednauty PRIVATE -fPIC)
endif()
if(UNIX AND NOT APPLE)
  target_link_libraries(orderednauty PRIVATE m)
endif()
