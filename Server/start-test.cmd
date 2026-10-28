@echo off
rem One-click test server for local multiplayer tests (Docs/Server.md).
rem Uses a dev-only JWT secret and a local test database (Server\data\test.db, ignored by git).
rem Do NOT expose this to the internet as is: for a public server make a .env with a real secret and run "npm start".
rem Optional: set PORT, JWT_SECRET or DB_PATH before running to override.
cd /d "%~dp0"
if not defined PORT set PORT=3000
if not defined JWT_SECRET set JWT_SECRET=dev-only-local-test-secret-0123456789abcdef
if not defined DB_PATH set DB_PATH=%~dp0data\test.db
if not exist "%~dp0node_modules" call npm install
echo Test server: http://localhost:%PORT%   database: %DB_PATH%
echo Stop with Ctrl+C. Delete the database file to reset every account.
node src/index.ts
pause
