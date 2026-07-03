# 02 RBAC Matrix — Update from Wave 3 Sanitized Evidence

## Add action set

Every feature/module should be tagged with explicit permissions:

- `read`
- `create`
- `update`
- `correct_void`
- `print`
- `approve`
- `export`
- `admin`

For financial documents, avoid normal `delete`; use `correct_void`, reversal, credit note or deactivation depending on document type.

## Add approval gates

Manager/admin approval required for:

- discount outside configured limits;
- price override;
- void/cancel after kitchen/fiscal/payment stage;
- table transfer between waiters where policy requires it;
- stock correction;
- write-off/scrap;
- period reopen;
- fiscal retry/manual intervention where required;
- cash shift adjustment;
- worker settlement correction.

## Add migration rule

Legacy PRO DATA users, passwords, PINs, MAC/device identifiers and hidden credentials are **not migrated**. Users are recreated/reset in iData+.

Legacy RBAC is used only as capability evidence to avoid missing controls.

## Add audit rule

Every critical action must produce append-only audit evidence:

- actor;
- business/branch;
- device/session;
- original event timestamp;
- sync timestamp when offline;
- before/after hashes or structured diff;
- reason;
- approval actor when applicable.
