---
name: testing-webdevloop
description: Use as the tester before starting WebDevLoop itself as the application under test, to give that instance its own data directory and database.
---

The host WebDevLoop that runs you may use the default data directory and its database. An instance you start inherits both, and a second instance on the same database refuses to start (`already in use by another WebDevLoop process`) or, worse, disrupts the host's running steps.

1. **Create a private data directory**, for example `app-data` inside your evidence directory.
2. **Set both variables in the shell that starts the application**, before the run instructions' start command:
   - `WebDevLoop__DataDirectory` = that directory;
   - `WebDevLoop__DatabasePath` = `webdevloop.db` inside it (the shell may already define a database path, which would override the directory).
3. **Start the application** as the run instructions describe, on the reserved port.
4. **Confirm the isolation**: the startup log says `data directory <your directory>`, and `webdevloop.db` exists there. Stop and fix the variables when either differs.

When the application exits with `already in use by another WebDevLoop process`, it started without step 2. Repeat steps 1 and 2 and start it again. The process holding the database is the host; leave it running.
