import { setupServer } from "msw/node";

// Shared MSW server for Node. No default handlers — every test file registers exactly the
// handlers it needs via `server.use(...)` in each test, so a test can never accidentally pass
// because of a handler left behind by a different test file.
export const server = setupServer();
