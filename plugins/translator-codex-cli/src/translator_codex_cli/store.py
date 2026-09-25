"""Gateway identity and in-flight transport receipts, independent of translation jobs."""
from contextlib import contextmanager
import hashlib
import json
from pathlib import Path
import secrets
import sqlite3
import time
import uuid

from translation_service.domain import canonical
from translation_service.errors import ServiceError

from .config import GatewaySettings


def digest(value: str) -> str:
    """Return SHA-256 digest of credential or canonical transport value."""
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


class GatewayStore:
    """Transactional registration and single-delivery reservation per gateway."""

    def __init__(self, settings: GatewaySettings):
        """Initialize plugin database using settings."""
        # Plugin configuration contains no translation request state.
        self.settings = settings
        # Resolved plugin database filename.
        self.path = Path(settings.database).resolve()
        self.path.parent.mkdir(parents=True, exist_ok=True)
        db = sqlite3.connect(self.path)
        try:
            if db.execute("SELECT 1 FROM sqlite_master WHERE name='jobs'").fetchone():
                raise RuntimeError("Legacy translator database is not a gateway database")
            db.execute("PRAGMA journal_mode=WAL")
            # Retain legacy caller columns for existing databases; device lookup uses client_instance only.
            db.executescript("""
                CREATE TABLE IF NOT EXISTS gateways (
                    id TEXT PRIMARY KEY, caller TEXT NOT NULL, client_instance TEXT NOT NULL,
                    installation_id TEXT NOT NULL, device_hash TEXT NOT NULL UNIQUE,
                    revoked INTEGER NOT NULL DEFAULT 0, heartbeat REAL NOT NULL DEFAULT 0,
                    readiness TEXT NOT NULL DEFAULT 'login_required', active_task TEXT,
                    active_delivery TEXT, models TEXT NOT NULL DEFAULT '[]',
                    models_at REAL NOT NULL DEFAULT 0, UNIQUE(caller,client_instance)
                );
                CREATE TABLE IF NOT EXISTS enrollments (
                    code_hash TEXT PRIMARY KEY, caller TEXT NOT NULL, client_instance TEXT NOT NULL,
                    expires_at REAL NOT NULL, installation_id TEXT, device_hash TEXT, gateway_id TEXT
                );
                CREATE TABLE IF NOT EXISTS deliveries (
                    id TEXT PRIMARY KEY, gateway_id TEXT NOT NULL REFERENCES gateways(id),
                    token TEXT NOT NULL, state TEXT NOT NULL, payload TEXT NOT NULL,
                    response TEXT, response_hash TEXT, deadline REAL NOT NULL,
                    owner_until REAL NOT NULL, created_at REAL NOT NULL
                );
                CREATE INDEX IF NOT EXISTS gateway_deliveries ON deliveries(gateway_id,state);
                PRAGMA user_version=1;
            """)
        finally:
            db.close()

    @contextmanager
    def connect(self):
        """Yield a transaction with serialized writes; always close connection."""
        db = sqlite3.connect(self.path, timeout=10)
        db.row_factory = sqlite3.Row
        try:
            db.execute("PRAGMA foreign_keys=ON")
            db.execute("BEGIN IMMEDIATE")
            yield db
            db.commit()
        except BaseException:
            db.rollback()
            raise
        finally:
            db.close()

    def expire(self, db, now: float) -> None:
        """Expire abandoned deliveries in db at now; no return value."""
        db.execute("""UPDATE deliveries SET state='discarded',payload='{}',response=NULL
                      WHERE state IN ('pending','leased','started','received') AND (deadline<=? OR owner_until<=?)""", (now, now))
        db.execute("""UPDATE gateways SET active_delivery=NULL WHERE active_delivery IN
                      (SELECT id FROM deliveries WHERE state IN ('accepted','discarded'))""")
        db.execute("DELETE FROM deliveries WHERE state IN ('accepted','discarded') AND created_at<?",
                   (now-self.settings.receipt_retention_seconds,))

    def device(self, db, client: str | None):
        """Return nonrevoked gateway in db for client or raise safe 404."""
        row = db.execute("SELECT * FROM gateways WHERE client_instance=? AND revoked=0", (client,)).fetchone()
        if row is None:
            raise ServiceError("client_instance", 404)
        return row

    def available(self, row, now: float) -> None:
        """Check row readiness at now; raise immediately when offline/busy."""
        if row["heartbeat"] <= now-self.settings.offline_seconds:
            raise ServiceError("gateway_offline", 503)
        if row["readiness"] != "ready":
            raise ServiceError("gateway_not_ready", 503)
        if row["active_delivery"] or row["active_task"]:
            raise ServiceError("gateway_busy", 409)

    def authenticate(self, token: str) -> dict:
        """Return nonrevoked gateway identified by device token."""
        with self.connect() as db:
            row = db.execute("SELECT * FROM gateways WHERE device_hash=? AND revoked=0", (digest(token),)).fetchone()
            if not token or row is None:
                raise ServiceError("gateway_credential", 401)
            return dict(row)

    def enrollment(self, client: str | None) -> tuple[str, str, float]:
        """Return client ID, one-use code, and expiry for requested device."""
        client, code, expiry = client or str(uuid.uuid4()), secrets.token_urlsafe(32), time.time()+1800
        with self.connect() as db:
            db.execute("INSERT INTO enrollments(code_hash,caller,client_instance,expires_at) VALUES(?,'',?,?)",
                       (digest(code), client, expiry))
        return client, code, expiry

    def registration(self, code: str) -> dict:
        """Return unused enrollment metadata for download code."""
        with self.connect() as db:
            row = db.execute("SELECT * FROM enrollments WHERE code_hash=?", (digest(code),)).fetchone()
            if not row or row["expires_at"] <= time.time() or row["gateway_id"]:
                raise ServiceError("enrollment_unavailable", 404)
            return dict(row)

    def enroll(self, code: str, installation: str, token: str) -> dict:
        """Return gateway registration for code, installation identity, and token."""
        hashed = digest(token)
        with self.connect() as db:
            row = db.execute("SELECT * FROM enrollments WHERE code_hash=?", (digest(code),)).fetchone()
            if not row:
                raise ServiceError("enrollment_invalid", 401)
            if row["gateway_id"]:
                gateway = db.execute("SELECT revoked FROM gateways WHERE id=?", (row["gateway_id"],)).fetchone()
                if row["installation_id"] == installation and row["device_hash"] == hashed and gateway and not gateway["revoked"]:
                    return {"gateway_id": row["gateway_id"], "client_instance_id": row["client_instance"]}
                raise ServiceError("enrollment_used", 409)
            if row["expires_at"] <= time.time():
                raise ServiceError("enrollment_expired", 401)
            existing = db.execute("SELECT * FROM gateways WHERE client_instance=?", (row["client_instance"],)).fetchone()
            if existing and not existing["revoked"]:
                raise ServiceError("client_already_registered", 409)
            identifier = existing["id"] if existing else str(uuid.uuid4())
            try:
                db.execute("""INSERT INTO gateways(id,caller,client_instance,installation_id,device_hash) VALUES(?,'',?,?,?)
                              ON CONFLICT(id) DO UPDATE SET installation_id=excluded.installation_id,
                              device_hash=excluded.device_hash,revoked=0,heartbeat=0,readiness='login_required',
                              active_task=NULL,active_delivery=NULL,models='[]',models_at=0""",
                           (identifier, row["client_instance"], installation, hashed))
            except sqlite3.IntegrityError:
                raise ServiceError("device_already_registered", 409) from None
            db.execute("UPDATE enrollments SET installation_id=?,device_hash=?,gateway_id=? WHERE code_hash=?",
                       (installation, hashed, identifier, digest(code)))
            return {"gateway_id": identifier, "client_instance_id": row["client_instance"]}

    def status(self, client: str) -> dict:
        """Return public connectivity status for client."""
        with self.connect() as db:
            self.expire(db, time.time())
            row = self.device(db, client)
            return {"client_instance_id": client, "online": row["heartbeat"] > time.time()-self.settings.offline_seconds,
                    "readiness": row["readiness"], "busy": bool(row["active_task"] or row["active_delivery"]),
                    "active_task": row["active_task"], "models_updated_at": row["models_at"]}

    def revoke(self, client: str) -> None:
        """Revoke client and discard in-flight work; no return value."""
        with self.connect() as db:
            row = self.device(db, client)
            db.execute("UPDATE gateways SET revoked=1,active_delivery=NULL WHERE id=?", (row["id"],))
            db.execute("UPDATE enrollments SET expires_at=0 WHERE client_instance=?", (client,))
            db.execute("UPDATE deliveries SET state='discarded',payload='{}',response=NULL WHERE gateway_id=?", (row["id"],))

    def heartbeat(self, gateway: str, readiness: str, active: str | None, models: list | None) -> list[str]:
        """Record gateway readiness/active/models and return cancellation IDs."""
        now, cancel = time.time(), []
        with self.connect() as db:
            self.expire(db, now)
            if active:
                row = db.execute("SELECT * FROM deliveries WHERE id=? AND gateway_id=?", (active, gateway)).fetchone()
                if not row or row["state"] == "discarded":
                    cancel.append(active)
                elif row["state"] in ("received", "accepted"):
                    active = None
            db.execute("UPDATE gateways SET heartbeat=?,readiness=?,active_task=? WHERE id=?", (now, readiness, active, gateway))
            if models is not None:
                db.execute("UPDATE gateways SET models=?,models_at=? WHERE id=?", (canonical(models), now, gateway))
        return cancel

    def models(self, client: str | None) -> list[dict]:
        """Return fresh available catalog for client, or fail immediately."""
        with self.connect() as db:
            self.expire(db, time.time())
            row = self.device(db, client)
            self.available(row, time.time())
            if row["models_at"] <= time.time()-self.settings.model_cache_seconds:
                raise ServiceError("model_cache_unavailable", 409)
            return json.loads(row["models"])

    def reserve(self, client: str | None, payload: dict) -> str:
        """Reserve one available gateway for payload and return delivery ID."""
        now = time.time()
        identifier = str(uuid.uuid4())
        with self.connect() as db:
            self.expire(db, now)
            row = self.device(db, client)
            self.available(row, now)
            db.execute("INSERT INTO deliveries VALUES(?,?,?,?,?,?,?,?,?,?)",
                       (identifier, row["id"], secrets.token_urlsafe(32), "pending", canonical(payload), None, None,
                        now+self.settings.turn_timeout_seconds, now+self.settings.request_lease_seconds, now))
            db.execute("UPDATE gateways SET active_delivery=? WHERE id=?", (identifier, row["id"]))
        return identifier

    def poll(self, identifier: str) -> dict | None:
        """Return completed delivery while renewing its live request-owner lease."""
        now = time.time()
        with self.connect() as db:
            self.expire(db, now)
            row = db.execute("SELECT * FROM deliveries WHERE id=?", (identifier,)).fetchone()
            if not row or row["state"] == "discarded":
                raise ServiceError("gateway_delivery_expired", 504)
            if row["state"] == "received":
                return json.loads(row["response"])
            if row["owner_until"] < now+self.settings.request_lease_seconds/2:
                db.execute("UPDATE deliveries SET owner_until=? WHERE id=?", (now+self.settings.request_lease_seconds, identifier))
        return None

    def finish(self, identifier: str, accepted: bool) -> None:
        """Release identifier and erase payload; preserve only receipt hash."""
        with self.connect() as db:
            db.execute("UPDATE deliveries SET state=?,payload='{}',response=NULL WHERE id=?",
                       ("accepted" if accepted else "discarded", identifier))
            db.execute("UPDATE gateways SET active_delivery=NULL WHERE active_delivery=?", (identifier,))

    def next(self, gateway: str) -> dict | None:
        """Return reserved gateway envelope without creating or scheduling work."""
        now = time.time()
        with self.connect() as db:
            self.expire(db, now)
            device = db.execute("SELECT * FROM gateways WHERE id=? AND revoked=0", (gateway,)).fetchone()
            if device is None:
                raise ServiceError("gateway_revoked", 401)
            if device["readiness"] != "ready" or device["heartbeat"] <= now-self.settings.offline_seconds:
                return None
            row = db.execute("SELECT * FROM deliveries WHERE id=? AND state IN ('pending','leased','started')",
                             (device["active_delivery"],)).fetchone()
            if row is None:
                return None
            state = "leased" if row["state"] == "pending" else row["state"]
            db.execute("UPDATE deliveries SET state=? WHERE id=?", (state, row["id"]))
            return {"id": row["id"], "lease_token": row["token"], "state": state, **json.loads(row["payload"])}

    def started(self, gateway: str, identifier: str, token: str) -> None:
        """Authorize one delivery start using gateway/token; no return value."""
        with self.connect() as db:
            self.expire(db, time.time())
            row = db.execute("SELECT * FROM deliveries WHERE id=? AND gateway_id=?", (identifier, gateway)).fetchone()
            if not row:
                raise ServiceError("task_discarded", 409)
            if not secrets.compare_digest(row["token"], token):
                raise ServiceError("task_not_found", 404)
            if row["state"] not in ("leased", "started"):
                raise ServiceError("task_discarded", 409)
            db.execute("UPDATE deliveries SET state='started' WHERE id=?", (identifier,))
            db.execute("UPDATE gateways SET active_task=? WHERE id=?", (identifier, gateway))

    def deliver(self, gateway: str, identifier: str, token: str, result: dict) -> str:
        """Return acknowledgement for authenticated, idempotent result delivery."""
        # Preserve malformed Unicode for core validation without storing invalid UTF-8.
        encoded = json.dumps(result, ensure_ascii=True, sort_keys=True, separators=(",", ":"), allow_nan=False)
        hashed = digest(encoded)
        with self.connect() as db:
            self.expire(db, time.time())
            row = db.execute("SELECT * FROM deliveries WHERE id=? AND gateway_id=?", (identifier, gateway)).fetchone()
            if row is None:
                return "discarded"
            if not secrets.compare_digest(row["token"], token):
                raise ServiceError("task_not_found", 404)
            db.execute("UPDATE gateways SET active_task=NULL WHERE id=? AND active_task=?", (gateway, identifier))
            if row["state"] == "discarded":
                return "discarded"
            if row["response_hash"]:
                if row["response_hash"] != hashed:
                    raise ServiceError("result_payload_conflict", 409)
                return "duplicate"
            if row["state"] != "started":
                raise ServiceError("task_not_started", 409)
            db.execute("UPDATE deliveries SET state='received',response=?,response_hash=? WHERE id=?", (encoded, hashed, identifier))
            return "accepted"
