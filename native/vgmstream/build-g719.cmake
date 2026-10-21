# vgmstream declares an imported target when the archive already exists.
# Request the concrete build target only for the initial build.
if(G719_ENABLED AND NOT EXISTS "${G719_BUILD_DIR}/dependencies/libg719_decode/libg719_decode.a")
  execute_process(COMMAND "${CMAKE_COMMAND}" --build "${G719_BUILD_DIR}" --config Release --target g719_decode
    RESULT_VARIABLE result)
  if(NOT result EQUAL 0)
    message(FATAL_ERROR "The native G.719 dependency did not build")
  endif()
endif()
