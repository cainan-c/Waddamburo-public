/* PS5 application mounts reject lstat even where stat/open work.
 * State database paths are absolute, under /data/Waddamburo. Disable SQLite
 * Unix path symlink expansion; actual open flags and database locking remain. */
#undef HAVE_LSTAT
#undef HAVE_READLINK

/* Application-created journals already belong to the app. The SDK fchown
 * fallback issues a raw Unix syscall, unavailable in this application context. */
#undef HAVE_FCHOWN
