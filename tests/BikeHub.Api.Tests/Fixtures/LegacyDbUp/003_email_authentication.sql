CREATE TABLE auth_accounts
(
    id UUID PRIMARY KEY,
    email TEXT NOT NULL UNIQUE CHECK (length(email) <= 254),
    display_name TEXT NOT NULL CHECK (length(display_name) BETWEEN 1 AND 60),
    email_confirmed_at TIMESTAMPTZ NOT NULL,
    phone_e164 TEXT NULL,
    phone_confirmed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX ix_auth_accounts_phone ON auth_accounts(phone_e164) WHERE phone_e164 IS NOT NULL;

CREATE TABLE auth_email_challenges
(
    id UUID PRIMARY KEY,
    email TEXT NOT NULL CHECK (length(email) <= 254),
    display_name TEXT NOT NULL CHECK (length(display_name) BETWEEN 1 AND 60),
    code_hash TEXT NOT NULL CHECK (length(code_hash) = 64),
    binding_hash TEXT NOT NULL CHECK (length(binding_hash) = 64),
    created_at TIMESTAMPTZ NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    attempts INTEGER NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    delivered BOOLEAN NOT NULL DEFAULT false,
    consumed BOOLEAN NOT NULL DEFAULT false
);

CREATE INDEX ix_auth_email_challenges_email_created ON auth_email_challenges(email, created_at);
CREATE INDEX ix_auth_email_challenges_created ON auth_email_challenges(created_at);
CREATE INDEX ix_auth_email_challenges_binding ON auth_email_challenges(binding_hash) WHERE NOT consumed;

CREATE TABLE auth_sessions
(
    id UUID PRIMARY KEY,
    account_id UUID NOT NULL REFERENCES auth_accounts(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX ix_auth_sessions_account ON auth_sessions(account_id);
CREATE INDEX ix_auth_sessions_expires ON auth_sessions(expires_at);
