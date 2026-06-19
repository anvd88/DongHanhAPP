USE [master];
GO

IF DB_ID(N'KetoanMini') IS NULL
BEGIN
    CREATE DATABASE [KetoanMini];
END
GO

IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'KetoanMini' AND is_read_committed_snapshot_on = 0)
BEGIN
    ALTER DATABASE [KetoanMini] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
END
GO

USE [KetoanMini];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.customers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.customers
    (
        id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_customers PRIMARY KEY,
        name NVARCHAR(255) NOT NULL,
        tax_code NVARCHAR(50) NOT NULL CONSTRAINT DF_customers_tax_code DEFAULT N'',
        phone NVARCHAR(50) NOT NULL CONSTRAINT DF_customers_phone DEFAULT N'',
        address NVARCHAR(500) NOT NULL CONSTRAINT DF_customers_address DEFAULT N'',
        note NVARCHAR(MAX) NOT NULL CONSTRAINT DF_customers_note DEFAULT N'',
        is_active BIT NOT NULL CONSTRAINT DF_customers_is_active DEFAULT 1,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_customers_created_at DEFAULT SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_customers_name' AND object_id = OBJECT_ID(N'dbo.customers'))
BEGIN
    CREATE UNIQUE INDEX UX_customers_name ON dbo.customers(name);
END
GO

IF OBJECT_ID(N'dbo.customer_aliases', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.customer_aliases
    (
        id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_customer_aliases PRIMARY KEY,
        customer_id UNIQUEIDENTIFIER NOT NULL,
        customer_name NVARCHAR(255) NOT NULL CONSTRAINT DF_customer_aliases_customer_name DEFAULT N'',
        alias NVARCHAR(255) NOT NULL CONSTRAINT DF_customer_aliases_alias DEFAULT N'',
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_customer_aliases_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_customer_aliases_customers FOREIGN KEY (customer_id) REFERENCES dbo.customers(id)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_customer_aliases_customer' AND object_id = OBJECT_ID(N'dbo.customer_aliases'))
BEGIN
    CREATE INDEX IX_customer_aliases_customer ON dbo.customer_aliases(customer_id);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_customer_aliases_alias' AND object_id = OBJECT_ID(N'dbo.customer_aliases'))
BEGIN
    CREATE UNIQUE INDEX UX_customer_aliases_alias ON dbo.customer_aliases(alias);
END
GO

IF OBJECT_ID(N'dbo.documents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.documents
    (
        id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_documents PRIMARY KEY,
        voucher_no NVARCHAR(50) NOT NULL,
        doc_date DATE NOT NULL,
        customer_id UNIQUEIDENTIFIER NOT NULL,
        customer_name NVARCHAR(255) NOT NULL CONSTRAINT DF_documents_customer_name DEFAULT N'',
        customer_input_name NVARCHAR(255) NOT NULL CONSTRAINT DF_documents_customer_input_name DEFAULT N'',
        content NVARCHAR(500) NOT NULL CONSTRAINT DF_documents_content DEFAULT N'',
        note NVARCHAR(MAX) NOT NULL CONSTRAINT DF_documents_note DEFAULT N'',
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_documents_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_documents_customers FOREIGN KEY (customer_id) REFERENCES dbo.customers(id)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_documents_date' AND object_id = OBJECT_ID(N'dbo.documents'))
BEGIN
    CREATE INDEX IX_documents_date ON dbo.documents(doc_date);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_documents_customer' AND object_id = OBJECT_ID(N'dbo.documents'))
BEGIN
    CREATE INDEX IX_documents_customer ON dbo.documents(customer_id);
END
GO

IF OBJECT_ID(N'dbo.document_lines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.document_lines
    (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_document_lines PRIMARY KEY,
        document_id UNIQUEIDENTIFIER NOT NULL,
        line_no INT NOT NULL,
        line_content NVARCHAR(500) NOT NULL CONSTRAINT DF_document_lines_line_content DEFAULT N'',
        category NVARCHAR(255) NOT NULL CONSTRAINT DF_document_lines_category DEFAULT N'',
        spec NVARCHAR(255) NOT NULL CONSTRAINT DF_document_lines_spec DEFAULT N'',
        quantity DECIMAL(18,3) NOT NULL CONSTRAINT DF_document_lines_quantity DEFAULT 0,
        unit_price DECIMAL(18,2) NOT NULL CONSTRAINT DF_document_lines_unit_price DEFAULT 0,
        note NVARCHAR(MAX) NOT NULL CONSTRAINT DF_document_lines_note DEFAULT N'',
        CONSTRAINT FK_document_lines_documents FOREIGN KEY (document_id) REFERENCES dbo.documents(id) ON DELETE CASCADE
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_document_lines_document' AND object_id = OBJECT_ID(N'dbo.document_lines'))
BEGIN
    CREATE INDEX IX_document_lines_document ON dbo.document_lines(document_id, line_no);
END
GO

IF OBJECT_ID(N'dbo.payments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.payments
    (
        id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_payments PRIMARY KEY,
        customer_id UNIQUEIDENTIFIER NOT NULL,
        customer_name NVARCHAR(255) NOT NULL CONSTRAINT DF_payments_customer_name DEFAULT N'',
        customer_input_name NVARCHAR(255) NOT NULL CONSTRAINT DF_payments_customer_input_name DEFAULT N'',
        pay_date DATE NOT NULL,
        content NVARCHAR(255) NOT NULL CONSTRAINT DF_payments_content DEFAULT N'',
        method NVARCHAR(100) NOT NULL CONSTRAINT DF_payments_method DEFAULT N'',
        account NVARCHAR(100) NOT NULL CONSTRAINT DF_payments_account DEFAULT N'',
        amount DECIMAL(18,2) NOT NULL CONSTRAINT DF_payments_amount DEFAULT 0,
        note NVARCHAR(MAX) NOT NULL CONSTRAINT DF_payments_note DEFAULT N'',
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_payments_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_payments_customers FOREIGN KEY (customer_id) REFERENCES dbo.customers(id)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_payments_date' AND object_id = OBJECT_ID(N'dbo.payments'))
BEGIN
    CREATE INDEX IX_payments_date ON dbo.payments(pay_date);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_payments_customer' AND object_id = OBJECT_ID(N'dbo.payments'))
BEGIN
    CREATE INDEX IX_payments_customer ON dbo.payments(customer_id);
END
GO

IF OBJECT_ID(N'dbo.app_users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.app_users
    (
        id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_app_users PRIMARY KEY,
        username NVARCHAR(100) NOT NULL,
        full_name NVARCHAR(255) NOT NULL CONSTRAINT DF_app_users_full_name DEFAULT N'',
        role NVARCHAR(50) NOT NULL CONSTRAINT DF_app_users_role DEFAULT N'User',
        password_hash NVARCHAR(500) NOT NULL CONSTRAINT DF_app_users_password_hash DEFAULT N'',
        is_active BIT NOT NULL CONSTRAINT DF_app_users_is_active DEFAULT 1,
        approval_status NVARCHAR(50) NOT NULL CONSTRAINT DF_app_users_approval_status DEFAULT N'Approved',
        approved_at DATETIME2(0) NULL,
        approved_by NVARCHAR(100) NOT NULL CONSTRAINT DF_app_users_approved_by DEFAULT N'',
        activation_code NVARCHAR(64) NOT NULL CONSTRAINT DF_app_users_activation_code DEFAULT N'',
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_app_users_created_at DEFAULT SYSUTCDATETIME()
    );
END
GO

IF COL_LENGTH(N'dbo.app_users', N'approval_status') IS NULL
BEGIN
    ALTER TABLE dbo.app_users ADD approval_status NVARCHAR(50) NOT NULL CONSTRAINT DF_app_users_approval_status DEFAULT N'Approved';
END
GO

IF COL_LENGTH(N'dbo.app_users', N'approved_at') IS NULL
BEGIN
    ALTER TABLE dbo.app_users ADD approved_at DATETIME2(0) NULL;
END
GO

IF COL_LENGTH(N'dbo.app_users', N'approved_by') IS NULL
BEGIN
    ALTER TABLE dbo.app_users ADD approved_by NVARCHAR(100) NOT NULL CONSTRAINT DF_app_users_approved_by DEFAULT N'';
END
GO

IF COL_LENGTH(N'dbo.app_users', N'activation_code') IS NULL
BEGIN
    ALTER TABLE dbo.app_users ADD activation_code NVARCHAR(64) NOT NULL CONSTRAINT DF_app_users_activation_code DEFAULT N'';
END
GO

IF COL_LENGTH(N'dbo.app_users', N'public_key') IS NULL
BEGIN
    ALTER TABLE dbo.app_users ADD public_key NVARCHAR(MAX) NOT NULL CONSTRAINT DF_app_users_public_key DEFAULT N'';
END
GO

IF COL_LENGTH(N'dbo.app_users', N'is_deleted') IS NULL
BEGIN
    ALTER TABLE dbo.app_users ADD is_deleted BIT NOT NULL CONSTRAINT DF_app_users_is_deleted DEFAULT 0;
END
GO

IF COL_LENGTH(N'dbo.app_users', N'deleted_at') IS NULL
BEGIN
    ALTER TABLE dbo.app_users ADD deleted_at DATETIME2(0) NULL;
END
GO

UPDATE dbo.app_users
SET approval_status = N'Approved'
WHERE approval_status = N'';
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_app_users_username' AND object_id = OBJECT_ID(N'dbo.app_users'))
BEGIN
    DROP INDEX UX_app_users_username ON dbo.app_users;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_app_users_username_active' AND object_id = OBJECT_ID(N'dbo.app_users'))
BEGIN
    CREATE UNIQUE INDEX UX_app_users_username_active ON dbo.app_users(username) WHERE is_deleted = 0;
END
GO

IF OBJECT_ID(N'dbo.registration_codes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.registration_codes
    (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_registration_codes PRIMARY KEY,
        code NVARCHAR(64) NOT NULL,
        note NVARCHAR(255) NOT NULL CONSTRAINT DF_registration_codes_note DEFAULT N'',
        is_active BIT NOT NULL CONSTRAINT DF_registration_codes_is_active DEFAULT 1,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_registration_codes_created_at DEFAULT SYSUTCDATETIME(),
        expires_at DATETIME2(0) NULL,
        created_by NVARCHAR(100) NOT NULL CONSTRAINT DF_registration_codes_created_by DEFAULT N'',
        used_at DATETIME2(0) NULL,
        used_by NVARCHAR(100) NOT NULL CONSTRAINT DF_registration_codes_used_by DEFAULT N''
    );
END
GO

IF COL_LENGTH(N'dbo.registration_codes', N'expires_at') IS NULL
BEGIN
    ALTER TABLE dbo.registration_codes ADD expires_at DATETIME2(0) NULL;
END
GO

UPDATE dbo.registration_codes
SET expires_at = DATEADD(HOUR, 1, created_at)
WHERE expires_at IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_registration_codes_code' AND object_id = OBJECT_ID(N'dbo.registration_codes'))
BEGIN
    CREATE UNIQUE INDEX UX_registration_codes_code ON dbo.registration_codes(code);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_registration_codes_active' AND object_id = OBJECT_ID(N'dbo.registration_codes'))
BEGIN
    CREATE INDEX IX_registration_codes_active ON dbo.registration_codes(is_active, used_at, created_at);
END
GO

IF OBJECT_ID(N'dbo.audit_logs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.audit_logs
    (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_audit_logs PRIMARY KEY,
        occurred_at DATETIME2(0) NOT NULL CONSTRAINT DF_audit_logs_occurred_at DEFAULT SYSUTCDATETIME(),
        user_id UNIQUEIDENTIFIER NULL,
        username NVARCHAR(100) NOT NULL CONSTRAINT DF_audit_logs_username DEFAULT N'',
        action NVARCHAR(255) NOT NULL CONSTRAINT DF_audit_logs_action DEFAULT N'',
        entity NVARCHAR(100) NOT NULL CONSTRAINT DF_audit_logs_entity DEFAULT N'',
        entity_name NVARCHAR(255) NOT NULL CONSTRAINT DF_audit_logs_entity_name DEFAULT N'',
        details NVARCHAR(MAX) NOT NULL CONSTRAINT DF_audit_logs_details DEFAULT N''
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_audit_logs_occurred' AND object_id = OBJECT_ID(N'dbo.audit_logs'))
BEGIN
    CREATE INDEX IX_audit_logs_occurred ON dbo.audit_logs(occurred_at);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_audit_logs_username' AND object_id = OBJECT_ID(N'dbo.audit_logs'))
BEGIN
    CREATE INDEX IX_audit_logs_username ON dbo.audit_logs(username);
END
GO

IF OBJECT_ID(N'dbo.password_reset_requests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.password_reset_requests
    (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_password_reset_requests PRIMARY KEY,
        requested_at DATETIME2(0) NOT NULL CONSTRAINT DF_password_reset_requests_requested_at DEFAULT SYSUTCDATETIME(),
        username NVARCHAR(100) NOT NULL CONSTRAINT DF_password_reset_requests_username DEFAULT N'',
        full_name NVARCHAR(255) NOT NULL CONSTRAINT DF_password_reset_requests_full_name DEFAULT N'',
        status NVARCHAR(50) NOT NULL CONSTRAINT DF_password_reset_requests_status DEFAULT N'Pending',
        resolved_at DATETIME2(0) NULL,
        resolved_by NVARCHAR(100) NOT NULL CONSTRAINT DF_password_reset_requests_resolved_by DEFAULT N''
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_password_reset_status' AND object_id = OBJECT_ID(N'dbo.password_reset_requests'))
BEGIN
    CREATE INDEX IX_password_reset_status ON dbo.password_reset_requests(status, requested_at);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_password_reset_username' AND object_id = OBJECT_ID(N'dbo.password_reset_requests'))
BEGIN
    CREATE INDEX IX_password_reset_username ON dbo.password_reset_requests(username);
END
GO

IF OBJECT_ID(N'dbo.work_access_requests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.work_access_requests
    (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_work_access_requests PRIMARY KEY,
        work_date DATE NOT NULL,
        requested_at DATETIME2(0) NOT NULL CONSTRAINT DF_work_access_requests_requested_at DEFAULT SYSUTCDATETIME(),
        username NVARCHAR(100) NOT NULL CONSTRAINT DF_work_access_requests_username DEFAULT N'',
        full_name NVARCHAR(255) NOT NULL CONSTRAINT DF_work_access_requests_full_name DEFAULT N'',
        access_slot NVARCHAR(50) NOT NULL CONSTRAINT DF_work_access_requests_access_slot DEFAULT N'',
        reason NVARCHAR(MAX) NOT NULL CONSTRAINT DF_work_access_requests_reason DEFAULT N'',
        status NVARCHAR(50) NOT NULL CONSTRAINT DF_work_access_requests_status DEFAULT N'Pending',
        approved_at DATETIME2(0) NULL,
        approved_by NVARCHAR(100) NOT NULL CONSTRAINT DF_work_access_requests_approved_by DEFAULT N''
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_work_access_status' AND object_id = OBJECT_ID(N'dbo.work_access_requests'))
BEGIN
    CREATE INDEX IX_work_access_status ON dbo.work_access_requests(status, requested_at);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_work_access_user_date_slot' AND object_id = OBJECT_ID(N'dbo.work_access_requests'))
BEGIN
    CREATE INDEX IX_work_access_user_date_slot ON dbo.work_access_requests(username, work_date, access_slot);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.app_users WHERE username = N'admin' AND is_deleted = 0)
BEGIN
    INSERT INTO dbo.app_users
        (id, username, full_name, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at)
    VALUES
        (NEWID(), N'admin', N'Quan tri he thong', N'Admin', N'PBKDF2$100000$5q8U8NEMvCHaJf2+GabOdQ==$niQaPFtnzn5fWNiNdMa0hTR4ViuWRqn2R6NzViqOgRk=', 1, N'Approved', SYSUTCDATETIME(), N'system', SYSUTCDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.audit_logs WHERE action = N'Init SQL Server database' AND entity = N'Database')
BEGIN
    INSERT INTO dbo.audit_logs
        (occurred_at, username, action, entity, entity_name, details)
    VALUES
        (SYSUTCDATETIME(), N'system', N'Init SQL Server database', N'Database', N'KetoanMini', N'Created SQL Server schema for KetoanMini.');
END
GO

IF OBJECT_ID(N'dbo.chat_conversations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.chat_conversations
    (
        id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_chat_conversations PRIMARY KEY,
        user_a NVARCHAR(100) NOT NULL,
        user_b NVARCHAR(100) NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_chat_conversations_created_at DEFAULT SYSUTCDATETIME()
    );
END
GO

IF COL_LENGTH(N'dbo.chat_conversations', N'user_a_id') IS NULL
BEGIN
    ALTER TABLE dbo.chat_conversations ADD user_a_id UNIQUEIDENTIFIER NULL;
END
GO

IF COL_LENGTH(N'dbo.chat_conversations', N'user_b_id') IS NULL
BEGIN
    ALTER TABLE dbo.chat_conversations ADD user_b_id UNIQUEIDENTIFIER NULL;
END
GO

IF COL_LENGTH(N'dbo.chat_conversations', N'is_deleted') IS NULL
BEGIN
    ALTER TABLE dbo.chat_conversations ADD is_deleted BIT NOT NULL CONSTRAINT DF_chat_conversations_is_deleted DEFAULT 0;
END
GO

IF COL_LENGTH(N'dbo.chat_conversations', N'deleted_at') IS NULL
BEGIN
    ALTER TABLE dbo.chat_conversations ADD deleted_at DATETIME2(0) NULL;
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_chat_conversations_pair' AND object_id = OBJECT_ID(N'dbo.chat_conversations'))
BEGIN
    DROP INDEX UX_chat_conversations_pair ON dbo.chat_conversations;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_chat_conversations_pair_ids' AND object_id = OBJECT_ID(N'dbo.chat_conversations'))
BEGIN
    CREATE UNIQUE INDEX UX_chat_conversations_pair_ids
        ON dbo.chat_conversations(user_a_id, user_b_id)
        WHERE is_deleted = 0 AND user_a_id IS NOT NULL AND user_b_id IS NOT NULL;
END
GO

IF OBJECT_ID(N'dbo.chat_messages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.chat_messages
    (
        id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_chat_messages PRIMARY KEY,
        conversation_id UNIQUEIDENTIFIER NOT NULL,
        sender_username NVARCHAR(100) NOT NULL,
        receiver_username NVARCHAR(100) NOT NULL,
        message_type NVARCHAR(40) NOT NULL CONSTRAINT DF_chat_messages_message_type DEFAULT N'Text',
        cipher_text NVARCHAR(MAX) NOT NULL CONSTRAINT DF_chat_messages_cipher_text DEFAULT N'',
        nonce NVARCHAR(100) NOT NULL CONSTRAINT DF_chat_messages_nonce DEFAULT N'',
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_chat_messages_created_at DEFAULT SYSUTCDATETIME(),
        status NVARCHAR(40) NOT NULL CONSTRAINT DF_chat_messages_status DEFAULT N'Sent',
        CONSTRAINT FK_chat_messages_conversations FOREIGN KEY (conversation_id) REFERENCES dbo.chat_conversations(id)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_chat_messages_conversation' AND object_id = OBJECT_ID(N'dbo.chat_messages'))
BEGIN
    CREATE INDEX IX_chat_messages_conversation ON dbo.chat_messages(conversation_id, created_at);
END
GO

IF COL_LENGTH(N'dbo.chat_messages', N'sender_id') IS NULL
BEGIN
    ALTER TABLE dbo.chat_messages ADD sender_id UNIQUEIDENTIFIER NULL;
END
GO

IF COL_LENGTH(N'dbo.chat_messages', N'receiver_id') IS NULL
BEGIN
    ALTER TABLE dbo.chat_messages ADD receiver_id UNIQUEIDENTIFIER NULL;
END
GO

IF COL_LENGTH(N'dbo.chat_messages', N'auth_tag') IS NULL
BEGIN
    ALTER TABLE dbo.chat_messages ADD auth_tag NVARCHAR(100) NOT NULL CONSTRAINT DF_chat_messages_auth_tag DEFAULT N'';
END
GO

IF COL_LENGTH(N'dbo.chat_messages', N'encrypted_key_for_sender') IS NULL
BEGIN
    ALTER TABLE dbo.chat_messages ADD encrypted_key_for_sender NVARCHAR(MAX) NOT NULL CONSTRAINT DF_chat_messages_encrypted_key_for_sender DEFAULT N'';
END
GO

IF COL_LENGTH(N'dbo.chat_messages', N'encrypted_key_for_receiver') IS NULL
BEGIN
    ALTER TABLE dbo.chat_messages ADD encrypted_key_for_receiver NVARCHAR(MAX) NOT NULL CONSTRAINT DF_chat_messages_encrypted_key_for_receiver DEFAULT N'';
END
GO

IF COL_LENGTH(N'dbo.chat_messages', N'file_size') IS NULL
BEGIN
    ALTER TABLE dbo.chat_messages ADD file_size BIGINT NOT NULL CONSTRAINT DF_chat_messages_file_size DEFAULT 0;
END
GO

IF COL_LENGTH(N'dbo.chat_messages', N'is_deleted') IS NULL
BEGIN
    ALTER TABLE dbo.chat_messages ADD is_deleted BIT NOT NULL CONSTRAINT DF_chat_messages_is_deleted DEFAULT 0;
END
GO

IF COL_LENGTH(N'dbo.chat_messages', N'deleted_at') IS NULL
BEGIN
    ALTER TABLE dbo.chat_messages ADD deleted_at DATETIME2(0) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_chat_messages_participants' AND object_id = OBJECT_ID(N'dbo.chat_messages'))
BEGIN
    CREATE INDEX IX_chat_messages_participants ON dbo.chat_messages(sender_id, receiver_id, created_at) WHERE is_deleted = 0;
END
GO

IF OBJECT_ID(N'dbo.chat_file_offers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.chat_file_offers
    (
        id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_chat_file_offers PRIMARY KEY,
        message_id UNIQUEIDENTIFIER NOT NULL,
        conversation_id UNIQUEIDENTIFIER NOT NULL,
        sender_username NVARCHAR(100) NOT NULL,
        receiver_username NVARCHAR(100) NOT NULL,
        encrypted_file_name NVARCHAR(MAX) NOT NULL CONSTRAINT DF_chat_file_offers_encrypted_file_name DEFAULT N'',
        file_name_nonce NVARCHAR(100) NOT NULL CONSTRAINT DF_chat_file_offers_file_name_nonce DEFAULT N'',
        file_size BIGINT NOT NULL CONSTRAINT DF_chat_file_offers_file_size DEFAULT 0,
        sender_ip NVARCHAR(64) NOT NULL CONSTRAINT DF_chat_file_offers_sender_ip DEFAULT N'',
        sender_port INT NOT NULL CONSTRAINT DF_chat_file_offers_sender_port DEFAULT 0,
        transfer_token NVARCHAR(128) NOT NULL CONSTRAINT DF_chat_file_offers_transfer_token DEFAULT N'',
        status NVARCHAR(40) NOT NULL CONSTRAINT DF_chat_file_offers_status DEFAULT N'Pending',
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_chat_file_offers_created_at DEFAULT SYSUTCDATETIME(),
        expires_at DATETIME2(0) NOT NULL,
        accepted_at DATETIME2(0) NULL,
        completed_at DATETIME2(0) NULL,
        CONSTRAINT FK_chat_file_offers_messages FOREIGN KEY (message_id) REFERENCES dbo.chat_messages(id),
        CONSTRAINT FK_chat_file_offers_conversations FOREIGN KEY (conversation_id) REFERENCES dbo.chat_conversations(id)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_chat_file_offers_receiver' AND object_id = OBJECT_ID(N'dbo.chat_file_offers'))
BEGIN
    CREATE INDEX IX_chat_file_offers_receiver ON dbo.chat_file_offers(receiver_username, status, expires_at);
END
GO

IF COL_LENGTH(N'dbo.chat_file_offers', N'sender_id') IS NULL
BEGIN
    ALTER TABLE dbo.chat_file_offers ADD sender_id UNIQUEIDENTIFIER NULL;
END
GO

IF COL_LENGTH(N'dbo.chat_file_offers', N'receiver_id') IS NULL
BEGIN
    ALTER TABLE dbo.chat_file_offers ADD receiver_id UNIQUEIDENTIFIER NULL;
END
GO

IF COL_LENGTH(N'dbo.chat_file_offers', N'is_deleted') IS NULL
BEGIN
    ALTER TABLE dbo.chat_file_offers ADD is_deleted BIT NOT NULL CONSTRAINT DF_chat_file_offers_is_deleted DEFAULT 0;
END
GO

IF COL_LENGTH(N'dbo.chat_file_offers', N'deleted_at') IS NULL
BEGIN
    ALTER TABLE dbo.chat_file_offers ADD deleted_at DATETIME2(0) NULL;
END
GO

IF OBJECT_ID(N'dbo.user_sessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.user_sessions
    (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_user_sessions PRIMARY KEY,
        session_token NVARCHAR(64) NOT NULL,
        username NVARCHAR(100) NOT NULL CONSTRAINT DF_user_sessions_username DEFAULT N'',
        machine_name NVARCHAR(255) NOT NULL CONSTRAINT DF_user_sessions_machine DEFAULT N'',
        started_at DATETIME2(0) NOT NULL CONSTRAINT DF_user_sessions_started DEFAULT SYSUTCDATETIME(),
        last_seen DATETIME2(0) NOT NULL CONSTRAINT DF_user_sessions_lastseen DEFAULT SYSUTCDATETIME(),
        ended_at DATETIME2(0) NULL,
        end_reason NVARCHAR(100) NOT NULL CONSTRAINT DF_user_sessions_endreason DEFAULT N'',
        is_active BIT NOT NULL CONSTRAINT DF_user_sessions_active DEFAULT 1
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_user_sessions_user' AND object_id = OBJECT_ID(N'dbo.user_sessions'))
BEGIN
    CREATE INDEX IX_user_sessions_user ON dbo.user_sessions(username, is_active, last_seen);
END
GO

-- Phân loại nguồn phiên: 'Desktop' (app WPF) hay 'Web' (trình duyệt). Nhờ cột này, cơ chế
-- single-login của desktop chỉ kết thúc phiên desktop khác, không đụng tới phiên web — và
-- bản web có thể ghi nhận hiện diện online để app desktop nhìn thấy.
IF COL_LENGTH(N'dbo.user_sessions', N'client_kind') IS NULL
BEGIN
    ALTER TABLE dbo.user_sessions ADD client_kind NVARCHAR(20) NOT NULL CONSTRAINT DF_user_sessions_client_kind DEFAULT N'Desktop';
END
GO

IF COL_LENGTH(N'dbo.work_access_requests', N'punch_at') IS NULL
BEGIN
    ALTER TABLE dbo.work_access_requests ADD punch_at DATETIME2(0) NULL;
END
GO

-- =========================================================================
-- Cập nhật phiên bản ứng dụng: cấu hình + lịch sử phát hành (releases)
-- =========================================================================

IF OBJECT_ID(N'dbo.app_settings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.app_settings
    (
        setting_key NVARCHAR(100) NOT NULL CONSTRAINT PK_app_settings PRIMARY KEY,
        setting_value NVARCHAR(MAX) NOT NULL CONSTRAINT DF_app_settings_value DEFAULT N'',
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_app_settings_updated_at DEFAULT SYSUTCDATETIME(),
        updated_by NVARCHAR(100) NOT NULL CONSTRAINT DF_app_settings_updated_by DEFAULT N''
    );
END
GO

-- Mặc định tắt chế độ chặn đăng nhập khi bản quá cũ.
IF NOT EXISTS (SELECT 1 FROM dbo.app_settings WHERE setting_key = N'update.enforce_block')
BEGIN
    INSERT INTO dbo.app_settings (setting_key, setting_value, updated_by)
    VALUES (N'update.enforce_block', N'0', N'system');
END
GO

IF OBJECT_ID(N'dbo.app_releases', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.app_releases
    (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_app_releases PRIMARY KEY,
        version NVARCHAR(50) NOT NULL,
        release_notes NVARCHAR(MAX) NOT NULL CONSTRAINT DF_app_releases_notes DEFAULT N'',
        setup_path NVARCHAR(500) NOT NULL CONSTRAINT DF_app_releases_setup_path DEFAULT N'',
        setup_file_name NVARCHAR(255) NOT NULL CONSTRAINT DF_app_releases_setup_file_name DEFAULT N'',
        setup_file VARBINARY(MAX) NULL,
        file_size BIGINT NOT NULL CONSTRAINT DF_app_releases_file_size DEFAULT 0,
        is_mandatory BIT NOT NULL CONSTRAINT DF_app_releases_is_mandatory DEFAULT 0,
        is_published BIT NOT NULL CONSTRAINT DF_app_releases_is_published DEFAULT 1,
        published_at DATETIME2(0) NOT NULL CONSTRAINT DF_app_releases_published_at DEFAULT SYSUTCDATETIME(),
        published_by NVARCHAR(100) NOT NULL CONSTRAINT DF_app_releases_published_by DEFAULT N'',
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_app_releases_created_at DEFAULT SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_app_releases_published' AND object_id = OBJECT_ID(N'dbo.app_releases'))
BEGIN
    CREATE INDEX IX_app_releases_published ON dbo.app_releases(is_published, id DESC);
END
GO
