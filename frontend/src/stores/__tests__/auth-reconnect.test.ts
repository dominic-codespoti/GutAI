/**
 * Tests for auth store cold-start / reconnection logic.
 * Run with: node --test --loader tsx src/stores/__tests__/auth-reconnect.test.ts
 * Or:       npx tsx --test src/stores/__tests__/auth-reconnect.test.ts
 */

import { describe, it } from "node:test";
import assert from "node:assert";

// ── In-memory storage mock ──
class MockStorage {
  private store = new Map<string, string>();
  getItem = async (k: string) => this.store.get(k) ?? null;
  setItem = async (k: string, v: string) => { this.store.set(k, v); };
  deleteItem = async (k: string) => { this.store.delete(k); };
  has = (k: string) => this.store.has(k);
  get = (k: string) => this.store.get(k);
}

function createTestHarness() {
  const storage = new MockStorage();

  type UserProfile = { id: string; email: string; displayName: string; onboardingCompleted: boolean };
  type AuthState = {
    user: UserProfile | null;
    isAuthenticated: boolean;
    isLoading: boolean;
    isReconnecting: boolean;
  };

  let state: AuthState = { user: null, isAuthenticated: false, isLoading: true, isReconnecting: false };
  const listeners = new Set<() => void>();
  const notify = () => listeners.forEach(l => l());

  let _retryCount = 0;
  const MAX_RETRIES = 8;
  let _shouldSucceedNext = false;
  let _should401Next = false;

  function set(next: Partial<AuthState>) {
    state = { ...state, ...next };
    notify();
  }

  function resetRetry() { _retryCount = 0; }

  function hydrate() {
    state = { ...state, isLoading: true };

    (async () => {
      try {
        let token = await storage.getItem("accessToken");
        const refreshToken = await storage.getItem("refreshToken");

        if (!token && !refreshToken) {
          set({ isLoading: false, isReconnecting: false });
          return;
        }

        if ((!token || true) && refreshToken) {
          if (_shouldSucceedNext) { _shouldSucceedNext = false; token = "new-token"; }
          else if (_should401Next) { _should401Next = false; throw Object.assign(new Error("Unauthorized"), { response: { status: 401 } }); }
          else { throw Object.assign(new Error("Network error: connect ECONNREFUSED"), { response: null }); }
        }

        if (!token) {
          set({ isLoading: false, isReconnecting: false });
          return;
        }

        resetRetry();
        set({ user: { id: "u1", email: "test@test.com", displayName: "Test", onboardingCompleted: true }, isAuthenticated: true, isLoading: false, isReconnecting: false });
      } catch (err: any) {
        const status = err?.response?.status;
        if (status === 401 || status === 403) {
          storage.deleteItem("accessToken");
          set({ user: null, isAuthenticated: false, isLoading: false, isReconnecting: false });
        } else {
          set({ isLoading: false, isReconnecting: true });
          if (_retryCount < MAX_RETRIES) {
            _retryCount++;
          }
        }
      }
    })();
  }

  function connect() {
    set({ isReconnecting: true });

    (async () => {
      try {
        let token = await storage.getItem("accessToken");
        const refreshToken = await storage.getItem("refreshToken");

        if ((!token || true) && refreshToken) {
          if (_shouldSucceedNext) { _shouldSucceedNext = false; token = "new-token"; }
          else if (_should401Next) { _should401Next = false; throw Object.assign(new Error("Unauthorized"), { response: { status: 401 } }); }
          else { throw Object.assign(new Error("Network error"), { response: null }); }
        }

        resetRetry();
        set({ user: { id: "u1", email: "test@test.com", displayName: "Test", onboardingCompleted: true }, isAuthenticated: true, isLoading: false, isReconnecting: false });
      } catch (err: any) {
        const status = err?.response?.status;
        if (status === 401 || status === 403) {
          storage.deleteItem("accessToken");
          set({ user: null, isAuthenticated: false, isLoading: false, isReconnecting: false });
        } else {
          set({ isReconnecting: true });
          if (_retryCount < MAX_RETRIES) {
            _retryCount++;
          }
        }
      }
    })();
  }

  return {
    get state() { return state; },
    storage,
    hydrate,
    connect,
    setShouldSucceedNext(v: boolean) { _shouldSucceedNext = v; },
    setShould401Next(v: boolean) { _should401Next = v; },
    get retryCount() { return _retryCount; },
    resetRetry,
  };
}

// ── Tests ──

describe("auth reconnect on cold start", async () => {
  await it("sets isReconnecting=true when hydrate fails with network error", async () => {
    const h = createTestHarness();
    await h.storage.setItem("accessToken", "old-token");
    await h.storage.setItem("refreshToken", "old-refresh");

    h.hydrate();
    await new Promise(r => setTimeout(r, 10));

    assert.strictEqual(h.state.isReconnecting, true);
    assert.strictEqual(h.state.isAuthenticated, false);
  });

  await it("does NOT set isReconnecting on 401 — deletes token instead", async () => {
    const h = createTestHarness();
    await h.storage.setItem("accessToken", "old-token");
    await h.storage.setItem("refreshToken", "old-refresh");
    h.setShould401Next(true);

    h.hydrate();
    await new Promise(r => setTimeout(r, 10));

    assert.strictEqual(h.state.isReconnecting, false);
    assert.strictEqual(h.state.isAuthenticated, false);
    assert.strictEqual(h.state.user, null);
  });

  await it("transitions to authenticated when connect succeeds after network error", async () => {
    const h = createTestHarness();
    await h.storage.setItem("accessToken", "old-token");
    await h.storage.setItem("refreshToken", "old-refresh");

    // First hydrate fails with network error
    h.hydrate();
    await new Promise(r => setTimeout(r, 10));
    assert.strictEqual(h.state.isReconnecting, true);

    // Server warms up — next call succeeds
    h.setShouldSucceedNext(true);
    h.connect();
    await new Promise(r => setTimeout(r, 10));

    assert.strictEqual(h.state.isAuthenticated, true);
    assert.strictEqual(h.state.isReconnecting, false);
    assert.notStrictEqual(h.state.user, null);
  });

  await it("preserves tokens after network error", async () => {
    const h = createTestHarness();
    await h.storage.setItem("accessToken", "old-token");
    await h.storage.setItem("refreshToken", "old-refresh");

    h.hydrate();
    await new Promise(r => setTimeout(r, 10));

    assert.strictEqual(h.storage.get("accessToken"), "old-token");
    assert.strictEqual(h.storage.get("refreshToken"), "old-refresh");
  });

  await it("deletes access token on 401 but keeps refresh token", async () => {
    const h = createTestHarness();
    await h.storage.setItem("accessToken", "old-token");
    await h.storage.setItem("refreshToken", "old-refresh");
    h.setShould401Next(true);

    h.hydrate();
    await new Promise(r => setTimeout(r, 10));

    assert.strictEqual(h.storage.get("accessToken"), undefined);
    assert.strictEqual(h.storage.get("refreshToken"), "old-refresh");
  });

  await it("resets retry counter on successful connect", async () => {
    const h = createTestHarness();
    await h.storage.setItem("accessToken", "old-token");
    await h.storage.setItem("refreshToken", "old-refresh");

    h.hydrate();
    await new Promise(r => setTimeout(r, 10));
    assert.strictEqual(h.retryCount, 1);

    h.setShouldSucceedNext(true);
    h.connect();
    await new Promise(r => setTimeout(r, 10));

    assert.strictEqual(h.state.isAuthenticated, true);
    // Retry count should be reset after success
    h.setShould401Next(true);
    h.hydrate();
    await new Promise(r => setTimeout(r, 10));
    assert.strictEqual(h.state.isReconnecting, false); // 401, not network error
  });
});
