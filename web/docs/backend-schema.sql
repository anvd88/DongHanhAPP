--
-- PostgreSQL database dump
--

\restrict 3iCACu3dwQW7dtrXlp2RW127fmzPXR02KdoXvt4obzWec3e2OfHsn824yNlUDyd

-- Dumped from database version 18.4
-- Dumped by pg_dump version 18.4

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: ketoanmini_publish_change(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.ketoanmini_publish_change() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
    scope_name text;
BEGIN
    FOREACH scope_name IN ARRAY TG_ARGV
    LOOP
        PERFORM pg_notify('ketoanmini_changes', scope_name);
    END LOOP;
    RETURN NULL;
END;
$$;

--
-- Name: prevent_cash_collection_event_mutation(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.prevent_cash_collection_event_mutation() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'cash_collection_events is append-only';
END;
$$;

--
-- Name: prevent_document_physical_delete(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.prevent_document_physical_delete() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'Không thể xóa vật lý phiếu kế toán; hãy chuyển phiếu sang trạng thái hủy.'
        USING ERRCODE = '23514';
END;
$$;

--
-- Name: prevent_hr_payout_voucher_event_mutation(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.prevent_hr_payout_voucher_event_mutation() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'hr_payout_voucher_events is append-only';
END;
$$;

--
-- Name: prevent_hr_payslip_history_mutation(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.prevent_hr_payslip_history_mutation() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'hr_payslip_history is append-only';
END;
$$;

--
-- Name: prevent_issued_warehouse_voucher_no_change(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.prevent_issued_warehouse_voucher_no_change() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF OLD.document_type = 'document'
       AND OLD.issued_at IS NOT NULL
       AND NEW.voucher_no IS DISTINCT FROM OLD.voucher_no THEN
        RAISE EXCEPTION 'Không thể thay đổi số phiếu xuất kho đã phát hành.'
            USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: app_config; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_config (
    id integer NOT NULL,
    announcement text DEFAULT ''::text NOT NULL,
    announcement_level character varying(16) DEFAULT 'info'::character varying NOT NULL,
    face_enroll_banner_enabled boolean DEFAULT true NOT NULL,
    foreground_poll_seconds integer DEFAULT 20 NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_by character varying(128) DEFAULT ''::character varying NOT NULL,
    portrait_height_factor double precision DEFAULT 1.85 NOT NULL,
    portrait_vertical_nudge double precision DEFAULT 0.15 NOT NULL,
    portrait_aspect double precision DEFAULT 0.75 NOT NULL,
    portrait_min_width_factor double precision DEFAULT 1.35 NOT NULL,

    feature_flags jsonb DEFAULT '{}'::jsonb NOT NULL,
    onboarding jsonb DEFAULT '{}'::jsonb NOT NULL,
    notices jsonb DEFAULT '[]'::jsonb NOT NULL,
    CONSTRAINT app_config_singleton CHECK ((id = 1))
);

--
-- Name: app_feedbacks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_feedbacks (
    id bigint NOT NULL,
    feedback_type character varying(32) NOT NULL,
    reporter_username character varying(128) NOT NULL,
    target_name character varying(256) DEFAULT ''::character varying NOT NULL,
    reason character varying(500) DEFAULT ''::character varying NOT NULL,
    conversation_id uuid,
    legacy_chat_report_id bigint,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: app_feedbacks_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.app_feedbacks_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: app_feedbacks_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.app_feedbacks_id_seq OWNED BY public.app_feedbacks.id;

--
-- Name: app_general_feedback; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_general_feedback (
    id uuid NOT NULL,
    username character varying(128) DEFAULT ''::character varying NOT NULL,
    anonymous boolean DEFAULT false NOT NULL,
    message text NOT NULL,
    status character varying(20) DEFAULT 'open'::character varying NOT NULL,
    response text DEFAULT ''::text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: app_outbox; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_outbox (
    id bigint NOT NULL,
    kind character varying(40) NOT NULL,
    payload jsonb NOT NULL,
    dedupe_key character varying(300) DEFAULT ''::character varying NOT NULL,
    status character varying(12) DEFAULT 'pending'::character varying NOT NULL,
    attempts integer DEFAULT 0 NOT NULL,
    available_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    processed_at timestamp with time zone,
    last_error text DEFAULT ''::text NOT NULL
);

--
-- Name: app_outbox_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.app_outbox_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: app_outbox_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.app_outbox_id_seq OWNED BY public.app_outbox.id;

--
-- Name: app_pin_codes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_pin_codes (
    username character varying(128) NOT NULL,
    pin_hash text NOT NULL,
    failed_attempts integer DEFAULT 0 NOT NULL,
    locked_until timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    last_verified_at timestamp with time zone
);

--
-- Name: app_portal_about; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_portal_about (
    id integer DEFAULT 1 NOT NULL,
    title character varying(300) DEFAULT ''::character varying NOT NULL,
    content text DEFAULT ''::text NOT NULL,
    cover_image text,
    address character varying(400) DEFAULT ''::character varying NOT NULL,
    hotline character varying(100) DEFAULT ''::character varying NOT NULL,
    email character varying(200) DEFAULT ''::character varying NOT NULL,
    website character varying(200) DEFAULT ''::character varying NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT app_portal_about_id_check CHECK ((id = 1))
);

--
-- Name: app_portal_posts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_portal_posts (
    id bigint NOT NULL,
    kind character varying(16) DEFAULT 'news'::character varying NOT NULL,
    title character varying(300) NOT NULL,
    summary character varying(600) DEFAULT ''::character varying NOT NULL,
    body text DEFAULT ''::text NOT NULL,
    cover_image text,
    location character varying(300) DEFAULT ''::character varying NOT NULL,
    event_at timestamp with time zone,
    pinned boolean DEFAULT false NOT NULL,
    published boolean DEFAULT true NOT NULL,
    author_username character varying(128) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: app_portal_posts_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.app_portal_posts_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: app_portal_posts_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.app_portal_posts_id_seq OWNED BY public.app_portal_posts.id;

--
-- Name: app_releases; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_releases (
    id bigint NOT NULL,
    app_target character varying(32) DEFAULT 'hr-apk'::character varying NOT NULL,
    version character varying(64) DEFAULT ''::character varying NOT NULL,
    version_code integer DEFAULT 1 NOT NULL,
    release_notes text DEFAULT ''::text NOT NULL,
    is_mandatory boolean DEFAULT false NOT NULL,
    is_published boolean DEFAULT false NOT NULL,
    apk_file_name character varying(256) DEFAULT ''::character varying NOT NULL,
    apk_mime_type character varying(128) DEFAULT 'application/vnd.android.package-archive'::character varying NOT NULL,
    apk_size bigint DEFAULT 0 NOT NULL,
    apk_sha256 character varying(64) DEFAULT ''::character varying NOT NULL,
    has_apk boolean DEFAULT false NOT NULL,
    apk_data bytea,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    published_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    published_by character varying(128) DEFAULT ''::character varying NOT NULL
);

--
-- Name: app_releases_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.app_releases_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: app_releases_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.app_releases_id_seq OWNED BY public.app_releases.id;

--
-- Name: app_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_settings (
    setting_key character varying(128) NOT NULL,
    setting_value text DEFAULT ''::text NOT NULL,
    updated_by character varying(128) DEFAULT ''::character varying NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: app_support_tickets; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_support_tickets (
    id uuid NOT NULL,
    ticket_code character varying(20) NOT NULL,
    username character varying(128) NOT NULL,
    message text NOT NULL,
    app_version character varying(40) DEFAULT ''::character varying NOT NULL,
    device_model character varying(160) DEFAULT ''::character varying NOT NULL,
    status character varying(20) DEFAULT 'open'::character varying NOT NULL,
    response text DEFAULT ''::text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: app_survey_responses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_survey_responses (
    id uuid NOT NULL,
    survey_id uuid NOT NULL,
    username character varying(128) NOT NULL,
    answers jsonb NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: app_surveys; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_surveys (
    id uuid NOT NULL,
    title character varying(200) NOT NULL,
    description text DEFAULT ''::text NOT NULL,
    questions jsonb DEFAULT '[]'::jsonb NOT NULL,
    active boolean DEFAULT true NOT NULL,
    closes_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: app_users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_users (
    id uuid NOT NULL,
    username character varying(128) NOT NULL,
    full_name character varying(256) DEFAULT ''::character varying NOT NULL,
    email character varying(256) DEFAULT ''::character varying NOT NULL,
    role character varying(32) DEFAULT 'Employee'::character varying NOT NULL,
    password_hash text NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    approval_status character varying(32) DEFAULT 'Approved'::character varying NOT NULL,
    approved_at timestamp with time zone,
    approved_by character varying(128) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    authorization_version integer DEFAULT 1 NOT NULL
);

--
-- Name: audit_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.audit_logs (
    id bigint NOT NULL,
    user_id uuid,
    occurred_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    username character varying(128) DEFAULT ''::character varying NOT NULL,
    action character varying(256) DEFAULT ''::character varying NOT NULL,
    entity character varying(128) DEFAULT ''::character varying NOT NULL,
    entity_name character varying(256) DEFAULT ''::character varying NOT NULL,
    details text DEFAULT ''::text NOT NULL,
    before_data jsonb,
    after_data jsonb
);

--
-- Name: audit_logs_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.audit_logs_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: audit_logs_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.audit_logs_id_seq OWNED BY public.audit_logs.id;

--
-- Name: cash_collection_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cash_collection_events (
    id uuid NOT NULL,
    order_id uuid NOT NULL,
    action character varying(48) NOT NULL,
    actor_username character varying(128) DEFAULT ''::character varying NOT NULL,
    before_status character varying(32),
    after_status character varying(32),
    note text DEFAULT ''::text NOT NULL,
    event_data jsonb,
    occurred_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: cash_collection_order_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cash_collection_order_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: cash_collection_orders; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cash_collection_orders (
    id uuid NOT NULL,
    order_no character varying(32) NOT NULL,
    customer_id uuid NOT NULL,
    customer_name character varying(256) DEFAULT ''::character varying NOT NULL,
    customer_phone character varying(64) DEFAULT ''::character varying NOT NULL,
    driver_employee_id uuid NOT NULL,
    driver_username character varying(128) DEFAULT ''::character varying NOT NULL,
    driver_name character varying(256) DEFAULT ''::character varying NOT NULL,
    expected_amount numeric(18,0) NOT NULL,
    scheduled_date date NOT NULL,
    handover_due_at timestamp with time zone NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    status character varying(32) DEFAULT 'Assigned'::character varying NOT NULL,
    created_by character varying(128) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    accepted_by character varying(128) DEFAULT ''::character varying NOT NULL,
    accepted_at timestamp with time zone,
    collected_by character varying(128) DEFAULT ''::character varying NOT NULL,
    collected_at timestamp with time zone,
    collected_amount numeric(18,0),
    failed_by character varying(128) DEFAULT ''::character varying NOT NULL,
    failed_at timestamp with time zone,
    failure_reason text DEFAULT ''::text NOT NULL,
    received_by character varying(128) DEFAULT ''::character varying NOT NULL,
    received_at timestamp with time zone,
    received_amount numeric(18,0),
    payment_id uuid,
    cancelled_by character varying(128) DEFAULT ''::character varying NOT NULL,
    cancelled_at timestamp with time zone,
    cancel_reason text DEFAULT ''::text NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: cash_count_lines; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cash_count_lines (
    id bigint NOT NULL,
    session_id uuid NOT NULL,
    denomination bigint NOT NULL,
    quantity integer NOT NULL,
    subtotal numeric(18,0) NOT NULL,
    CONSTRAINT cash_count_lines_check CHECK (((denomination > 0) AND (quantity > 0) AND (subtotal > (0)::numeric)))
);

--
-- Name: cash_count_lines_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cash_count_lines_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: cash_count_lines_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cash_count_lines_id_seq OWNED BY public.cash_count_lines.id;

--
-- Name: cash_count_sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cash_count_sessions (
    id uuid NOT NULL,
    order_id uuid NOT NULL,
    stage character varying(20) NOT NULL,
    revision integer NOT NULL,
    actor_username character varying(128) DEFAULT ''::character varying NOT NULL,
    total numeric(18,0) NOT NULL,
    confirmed_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: cash_fund_entry_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cash_fund_entry_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: cash_fund_manual_entries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cash_fund_manual_entries (
    id uuid NOT NULL,
    entry_no character varying(32) NOT NULL,
    direction character varying(8) NOT NULL,
    amount numeric(18,0) NOT NULL,
    occurred_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    reason character varying(256) DEFAULT ''::character varying NOT NULL,
    counterparty character varying(256) DEFAULT ''::character varying NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    is_opening boolean DEFAULT false NOT NULL,
    created_by character varying(128) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    reversed_at timestamp with time zone,
    reversed_by character varying(128) DEFAULT ''::character varying NOT NULL,
    reverse_reason text DEFAULT ''::text NOT NULL,
    CONSTRAINT cash_fund_manual_entries_amount_check CHECK ((amount > (0)::numeric)),
    CONSTRAINT cash_fund_manual_entries_direction_check CHECK (((direction)::text = ANY ((ARRAY['in'::character varying, 'out'::character varying])::text[])))
);

--
-- Name: document_lines; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.document_lines (
    id bigint NOT NULL,
    document_id uuid NOT NULL,
    line_no integer DEFAULT 0 NOT NULL,
    line_content text DEFAULT ''::text NOT NULL,
    category character varying(128) DEFAULT ''::character varying NOT NULL,
    spec text DEFAULT ''::text NOT NULL,
    quantity numeric(18,2) DEFAULT 0 NOT NULL,
    unit_price numeric(18,2) DEFAULT 0 NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    source_document_id uuid,
    source_line_no integer,
    product_id uuid
);

--
-- Name: documents; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.documents (
    id uuid NOT NULL,
    voucher_no character varying(64) NOT NULL,
    doc_date date NOT NULL,
    customer_id uuid,
    customer_name character varying(256) DEFAULT ''::character varying NOT NULL,
    customer_input_name character varying(256) DEFAULT ''::character varying NOT NULL,
    document_type character varying(16) DEFAULT 'document'::character varying NOT NULL,
    content text DEFAULT ''::text NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    issued_at timestamp with time zone,
    cancelled_at timestamp with time zone,
    cancelled_by character varying(128) DEFAULT ''::character varying NOT NULL,
    cancel_reason text DEFAULT ''::text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    delivery_mode character varying(16) DEFAULT ''::character varying NOT NULL,
    delivery_driver_username character varying(128) DEFAULT ''::character varying NOT NULL,
    delivery_driver_name character varying(200) DEFAULT ''::character varying NOT NULL,
    delivery_assigned_at timestamp with time zone,
    delivery_assigned_by character varying(128) DEFAULT ''::character varying NOT NULL,
    delivery_note text DEFAULT ''::text NOT NULL,
    delivery_task_id uuid,
    delivery_returned_at timestamp with time zone,
    delivery_returned_by character varying(128) DEFAULT ''::character varying NOT NULL,
    delivery_return_note text DEFAULT ''::text NOT NULL,
    CONSTRAINT ck_documents_delivery_mode CHECK ((((delivery_mode)::text = ANY ((ARRAY[''::character varying, 'driver'::character varying, 'pickup'::character varying])::text[])) AND (((delivery_mode)::text <> 'driver'::text) OR (btrim((delivery_driver_username)::text) <> ''::text)) AND (((delivery_mode)::text <> 'pickup'::text) OR (btrim((delivery_driver_username)::text) = ''::text)))),
    CONSTRAINT ck_documents_warehouse_issue_number CHECK ((((document_type)::text <> 'document'::text) OR ((issued_at IS NULL) AND ((voucher_no)::text = ''::text)) OR ((issued_at IS NOT NULL) AND (btrim((voucher_no)::text) <> ''::text))))
);

--
-- Name: hr_employees; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_employees (
    id uuid NOT NULL,
    employee_code character varying(32) DEFAULT ''::character varying NOT NULL,
    user_id uuid,
    username character varying(128) DEFAULT ''::character varying NOT NULL,
    full_name character varying(200) DEFAULT ''::character varying NOT NULL,
    dob date,
    gender character varying(16) DEFAULT ''::character varying NOT NULL,
    phone character varying(32) DEFAULT ''::character varying NOT NULL,
    email character varying(200) DEFAULT ''::character varying NOT NULL,
    address text DEFAULT ''::text NOT NULL,
    department_id uuid,
    "position" character varying(120) DEFAULT ''::character varying NOT NULL,
    manager_id uuid,
    hire_date date,
    status character varying(20) DEFAULT 'Active'::character varying NOT NULL,
    avatar text,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    show_phone_in_directory boolean DEFAULT false NOT NULL,
    show_email_in_directory boolean DEFAULT false NOT NULL,
    location_id uuid,
    access_role character varying(24) DEFAULT 'staff'::character varying NOT NULL,
    position_id uuid
);

--
-- Name: hr_payout_categories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_payout_categories (
    id uuid NOT NULL,
    code character varying(40) NOT NULL,
    name character varying(120) DEFAULT ''::character varying NOT NULL,
    description text DEFAULT ''::text NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    is_system boolean DEFAULT false NOT NULL,
    sort_order integer DEFAULT 100 NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_payout_vouchers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_payout_vouchers (
    id uuid NOT NULL,
    voucher_no character varying(20) DEFAULT ''::character varying NOT NULL,
    category_id uuid,
    employee_id uuid NOT NULL,
    amount numeric(18,2) DEFAULT 0 NOT NULL,
    source_kind character varying(16) DEFAULT 'manual'::character varying NOT NULL,
    source_id uuid,
    source_no character varying(32) DEFAULT ''::character varying NOT NULL,
    reason text DEFAULT ''::text NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    status character varying(20) DEFAULT 'AwaitingScan'::character varying NOT NULL,
    qr_code character varying(64) DEFAULT ''::character varying NOT NULL,
    qr_expires_at timestamp with time zone,
    created_by character varying(128) DEFAULT ''::character varying NOT NULL,
    requires_recipient_confirmation boolean DEFAULT true NOT NULL,
    confirmed_at timestamp with time zone,
    confirmed_by character varying(128) DEFAULT ''::character varying NOT NULL,
    approved_by character varying(128) DEFAULT ''::character varying NOT NULL,
    approved_at timestamp with time zone,
    paid_at timestamp with time zone,
    completed_by character varying(128) DEFAULT ''::character varying NOT NULL,
    completed_at timestamp with time zone,
    rejected_by character varying(128) DEFAULT ''::character varying NOT NULL,
    rejected_at timestamp with time zone,
    reject_reason text DEFAULT ''::text NOT NULL,
    cancelled_by character varying(128) DEFAULT ''::character varying NOT NULL,
    cancelled_at timestamp with time zone,
    cancel_reason text DEFAULT ''::text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: cash_fund_ledger; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.cash_fund_ledger AS
 SELECT o.id AS source_id,
    'collection'::character varying(24) AS source_kind,
    (o.order_no)::character varying(64) AS source_ref,
    'in'::character varying(8) AS direction,
    (COALESCE(o.received_amount, o.collected_amount, (0)::numeric))::numeric(18,0) AS amount,
    COALESCE(o.received_at, o.updated_at) AS occurred_at,
    'Thu tiền khách hàng'::character varying(256) AS reason,
    o.customer_name AS counterparty,
    o.received_by AS actor,
    ('Tài xế '::text || (o.driver_name)::text) AS note
   FROM public.cash_collection_orders o
  WHERE ((o.status)::text = 'Completed'::text)
UNION ALL
 SELECT v.id AS source_id,
    'payout'::character varying AS source_kind,
    (v.voucher_no)::character varying(64) AS source_ref,
    'out'::character varying AS direction,
    (v.amount)::numeric(18,0) AS amount,
    COALESCE(v.paid_at, v.completed_at, v.updated_at) AS occurred_at,
    (COALESCE(NULLIF((c.name)::text, ''::text), 'Chi tiền mặt'::text))::character varying(256) AS reason,
    (COALESCE(e.full_name, ''::character varying))::character varying(256) AS counterparty,
    v.completed_by AS actor,
    v.reason AS note
   FROM ((public.hr_payout_vouchers v
     LEFT JOIN public.hr_payout_categories c ON ((c.id = v.category_id)))
     LEFT JOIN public.hr_employees e ON ((e.id = v.employee_id)))
  WHERE ((v.status)::text = 'Paid'::text)
UNION ALL
 SELECT d.id AS source_id,
        CASE
            WHEN ((d.document_type)::text = 'receipt'::text) THEN 'receipt'::text
            ELSE 'payment'::text
        END AS source_kind,
    d.voucher_no AS source_ref,
        CASE
            WHEN ((d.document_type)::text = 'receipt'::text) THEN 'in'::text
            ELSE 'out'::text
        END AS direction,
    (( SELECT COALESCE(sum((l.quantity * l.unit_price)), (0)::numeric) AS "coalesce"
           FROM public.document_lines l
          WHERE (l.document_id = d.id)))::numeric(18,0) AS amount,
    (d.doc_date)::timestamp with time zone AS occurred_at,
    (COALESCE(NULLIF(d.content, ''::text), 'Phiếu thu chi'::text))::character varying(256) AS reason,
    d.customer_name AS counterparty,
    ''::character varying(128) AS actor,
    d.note
   FROM public.documents d
  WHERE (((d.document_type)::text = ANY ((ARRAY['receipt'::character varying, 'payment'::character varying])::text[])) AND (d.cancelled_at IS NULL))
UNION ALL
 SELECT m.id AS source_id,
    'manual'::character varying AS source_kind,
    (m.entry_no)::character varying(64) AS source_ref,
    m.direction,
    m.amount,
    m.occurred_at,
    m.reason,
    m.counterparty,
    m.created_by AS actor,
    m.note
   FROM public.cash_fund_manual_entries m
  WHERE (m.reversed_at IS NULL);

--
-- Name: cham_cong_face; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cham_cong_face (
    id bigint NOT NULL,
    username character varying(100) NOT NULL,
    full_name character varying(200) DEFAULT ''::character varying NOT NULL,
    embedding bytea NOT NULL,
    anh text,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    created_by character varying(100) DEFAULT ''::character varying NOT NULL,
    CONSTRAINT ck_cham_cong_face_embedding_kme1 CHECK ((SUBSTRING(embedding FROM 1 FOR 4) = '\x4b4d4531'::bytea))
);

--
-- Name: cham_cong_face_enrollment_samples; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cham_cong_face_enrollment_samples (
    id bigint NOT NULL,
    request_id uuid NOT NULL,
    pose character varying(20) NOT NULL,
    embedding bytea NOT NULL,
    quality double precision DEFAULT 0 NOT NULL,
    liveness double precision DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT cham_cong_face_enrollment_samples_embedding_check CHECK ((SUBSTRING(embedding FROM 1 FOR 4) = '\x4b4d4531'::bytea))
);

--
-- Name: cham_cong_face_enrollment_samples_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cham_cong_face_enrollment_samples_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: cham_cong_face_enrollment_samples_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cham_cong_face_enrollment_samples_id_seq OWNED BY public.cham_cong_face_enrollment_samples.id;

--
-- Name: cham_cong_face_enrollments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cham_cong_face_enrollments (
    id uuid NOT NULL,
    username character varying(100) NOT NULL,
    full_name character varying(200) DEFAULT ''::character varying NOT NULL,
    status character varying(20) DEFAULT 'pending'::character varying NOT NULL,
    sample_count integer DEFAULT 0 NOT NULL,
    requested_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    expires_at timestamp with time zone DEFAULT (CURRENT_TIMESTAMP + '14 days'::interval) NOT NULL,
    reviewed_by character varying(100) DEFAULT ''::character varying NOT NULL,
    reviewed_at timestamp with time zone,
    review_note character varying(500) DEFAULT ''::character varying NOT NULL,
    identity_verification_method character varying(40) DEFAULT ''::character varying CONSTRAINT cham_cong_face_enrollments_identity_verification_metho_not_null NOT NULL,
    CONSTRAINT cham_cong_face_enrollments_status_check CHECK (((status)::text = ANY ((ARRAY['pending'::character varying, 'approved'::character varying, 'rejected'::character varying, 'expired'::character varying])::text[])))
);

--
-- Name: cham_cong_face_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cham_cong_face_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: cham_cong_face_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cham_cong_face_id_seq OWNED BY public.cham_cong_face.id;

--
-- Name: cham_cong_log; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cham_cong_log (
    id bigint NOT NULL,
    username character varying(100) NOT NULL,
    full_name character varying(200) DEFAULT ''::character varying NOT NULL,
    loai character varying(10) NOT NULL,
    similarity double precision DEFAULT 0 NOT NULL,
    anh text,
    occurred_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    ghi_chu character varying(500) DEFAULT ''::character varying NOT NULL
);

--
-- Name: cham_cong_log_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cham_cong_log_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: cham_cong_log_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cham_cong_log_id_seq OWNED BY public.cham_cong_log.id;

--
-- Name: cham_cong_offline; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cham_cong_offline (
    id bigint NOT NULL,
    username character varying(100) NOT NULL,
    full_name character varying(200) DEFAULT ''::character varying NOT NULL,
    loai character varying(10) NOT NULL,
    similarity double precision DEFAULT 0 NOT NULL,
    quality double precision DEFAULT 0 NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    synced_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    backdate_minutes integer DEFAULT 0 NOT NULL,
    client_ip character varying(64) DEFAULT ''::character varying NOT NULL,
    on_company_lan boolean DEFAULT false NOT NULL,
    gps_lat double precision,
    gps_lng double precision,
    distance_m double precision,
    in_geofence boolean,
    flags character varying(400) DEFAULT ''::character varying NOT NULL,
    status character varying(20) DEFAULT 'pending'::character varying NOT NULL,
    reviewed_by character varying(100) DEFAULT ''::character varying NOT NULL,
    reviewed_at timestamp with time zone,
    review_note character varying(500) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: cham_cong_offline_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cham_cong_offline_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: cham_cong_offline_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cham_cong_offline_id_seq OWNED BY public.cham_cong_offline.id;

--
-- Name: cham_cong_qr_sites; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cham_cong_qr_sites (
    id uuid NOT NULL,
    name character varying(160) NOT NULL,
    project_name character varying(160) DEFAULT ''::character varying NOT NULL,
    qr_token character varying(120) NOT NULL,
    active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: customer_aliases; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customer_aliases (
    id bigint NOT NULL,
    customer_id uuid,
    customer_name character varying(256) DEFAULT ''::character varying NOT NULL,
    alias character varying(256) DEFAULT ''::character varying NOT NULL
);

--
-- Name: customer_aliases_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.customer_aliases_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: customer_aliases_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.customer_aliases_id_seq OWNED BY public.customer_aliases.id;

--
-- Name: customer_opening_balances; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customer_opening_balances (
    customer_id uuid NOT NULL,
    amount numeric(18,2) DEFAULT 0 NOT NULL,
    as_of_date date NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    updated_by character varying(128) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: customers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customers (
    id uuid NOT NULL,
    name character varying(256) NOT NULL,
    tax_code character varying(64) DEFAULT ''::character varying NOT NULL,
    phone character varying(64) DEFAULT ''::character varying NOT NULL,
    address text DEFAULT ''::text NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: document_issued_lines; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.document_issued_lines (
    document_id uuid NOT NULL,
    line_no integer NOT NULL,
    line_content text DEFAULT ''::text NOT NULL,
    spec text DEFAULT ''::text NOT NULL,
    quantity numeric(18,2) DEFAULT 0 NOT NULL,
    unit_price numeric(18,2) DEFAULT 0 NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    captured_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: document_line_edits; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.document_line_edits (
    id bigint NOT NULL,
    document_id uuid NOT NULL,
    line_no integer DEFAULT 0 NOT NULL,
    line_content text DEFAULT ''::text NOT NULL,
    old_quantity numeric(18,2) DEFAULT 0 NOT NULL,
    new_quantity numeric(18,2) DEFAULT 0 NOT NULL,
    old_unit_price numeric(18,2) DEFAULT 0 NOT NULL,
    new_unit_price numeric(18,2) DEFAULT 0 NOT NULL,
    reason text DEFAULT ''::text NOT NULL,
    actor_username character varying(128) DEFAULT ''::character varying NOT NULL,
    actor_name character varying(200) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: document_line_edits_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.document_line_edits_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: document_line_edits_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.document_line_edits_id_seq OWNED BY public.document_line_edits.id;

--
-- Name: document_lines_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.document_lines_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: document_lines_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.document_lines_id_seq OWNED BY public.document_lines.id;

--
-- Name: gia_cong_hang_hoa; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.gia_cong_hang_hoa (
    id bigint NOT NULL,
    phieu_id bigint NOT NULL,
    loai_dong character varying(50) DEFAULT 'Xuất gia công'::character varying NOT NULL,
    ma_hang character varying(50) DEFAULT ''::character varying NOT NULL,
    ten_hang character varying(200) DEFAULT ''::character varying NOT NULL,
    quy_cach character varying(200) DEFAULT ''::character varying NOT NULL,
    don_vi_tinh character varying(30) DEFAULT ''::character varying NOT NULL,
    so_luong numeric(18,2) DEFAULT 0 NOT NULL,
    don_gia_gia_cong numeric(18,2) DEFAULT 0 NOT NULL,
    ghi_chu text DEFAULT ''::text NOT NULL
);

--
-- Name: gia_cong_hang_hoa_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.gia_cong_hang_hoa_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: gia_cong_hang_hoa_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.gia_cong_hang_hoa_id_seq OWNED BY public.gia_cong_hang_hoa.id;

--
-- Name: gia_cong_phieu; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.gia_cong_phieu (
    id bigint NOT NULL,
    ma_phieu character varying(20) NOT NULL,
    loai_phieu character varying(50) NOT NULL,
    doi_tac character varying(200) DEFAULT ''::character varying NOT NULL,
    nhan_vien character varying(200) DEFAULT ''::character varying NOT NULL,
    ngay_lap date NOT NULL,
    han_hoan_thanh date,
    ghi_chu text DEFAULT ''::text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: gia_cong_phieu_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.gia_cong_phieu_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: gia_cong_phieu_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.gia_cong_phieu_id_seq OWNED BY public.gia_cong_phieu.id;

--
-- Name: goods_return_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.goods_return_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: help_faqs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.help_faqs (
    id uuid NOT NULL,
    category character varying(80) DEFAULT ''::character varying NOT NULL,
    question text DEFAULT ''::text NOT NULL,
    answer text DEFAULT ''::text NOT NULL,
    order_no integer DEFAULT 0 NOT NULL,
    is_published boolean DEFAULT true NOT NULL,
    updated_by character varying(128) DEFAULT ''::character varying NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_anniversary_letter; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_anniversary_letter (
    id smallint DEFAULT 1 NOT NULL,
    enabled boolean DEFAULT true NOT NULL,
    title text DEFAULT ''::text NOT NULL,
    body text DEFAULT ''::text NOT NULL,
    signature text DEFAULT ''::text NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT hr_anniversary_letter_id_check CHECK ((id = 1))
);

--
-- Name: hr_approval_delegations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_approval_delegations (
    from_username character varying(128) NOT NULL,
    to_username character varying(128) NOT NULL,
    from_date date NOT NULL,
    to_date date NOT NULL,
    active boolean DEFAULT true NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_attendance_corrections; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_attendance_corrections (
    id bigint NOT NULL,
    request_id uuid NOT NULL,
    request_no character varying(20) NOT NULL,
    employee_id uuid NOT NULL,
    username character varying(128) NOT NULL,
    full_name character varying(200) DEFAULT ''::character varying NOT NULL,
    work_date date NOT NULL,
    loai character varying(10) NOT NULL,
    corrected_time time without time zone NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    previous_occurred_at timestamp with time zone,
    previous_source character varying(24) DEFAULT 'missing'::character varying NOT NULL,
    reason text DEFAULT ''::text NOT NULL,
    approved_by character varying(128) NOT NULL,
    applied_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT hr_attendance_corrections_loai_check CHECK (((loai)::text = ANY ((ARRAY['Vào'::character varying, 'Ra'::character varying])::text[])))
);

--
-- Name: hr_attendance_corrections_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.hr_attendance_corrections_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: hr_attendance_corrections_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.hr_attendance_corrections_id_seq OWNED BY public.hr_attendance_corrections.id;

--
-- Name: hr_attendance_reminders; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_attendance_reminders (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    username character varying(128) NOT NULL,
    work_date date NOT NULL,
    direction character varying(8) NOT NULL,
    status character varying(24) DEFAULT 'Pending'::character varying NOT NULL,
    request_id uuid,
    notification_id character varying(160) NOT NULL,
    detected_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    last_checked_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    notification_enqueued_at timestamp with time zone,
    resolved_at timestamp with time zone,
    resolution_source character varying(32) DEFAULT ''::character varying NOT NULL,
    CONSTRAINT hr_attendance_reminders_direction_check CHECK (((direction)::text = ANY ((ARRAY['in'::character varying, 'out'::character varying])::text[]))),
    CONSTRAINT hr_attendance_reminders_status_check CHECK (((status)::text = ANY ((ARRAY['Pending'::character varying, 'RequestCreated'::character varying, 'Resolved'::character varying])::text[])))
);

--
-- Name: hr_bank_accounts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_bank_accounts (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    bank character varying(32) DEFAULT 'vietcombank'::character varying NOT NULL,
    account_number character varying(40) DEFAULT ''::character varying NOT NULL,
    account_holder character varying(200) DEFAULT ''::character varying NOT NULL,
    branch character varying(200) DEFAULT ''::character varying NOT NULL,
    is_default boolean DEFAULT false NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_contract_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.hr_contract_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: hr_contracts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_contracts (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    contract_no character varying(64) DEFAULT ''::character varying NOT NULL,
    contract_type character varying(64) DEFAULT ''::character varying NOT NULL,
    start_date date,
    end_date date,
    base_salary numeric(18,2) DEFAULT 0 NOT NULL,
    allowance numeric(18,2) DEFAULT 0 NOT NULL,
    status character varying(20) DEFAULT 'Active'::character varying NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_departments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_departments (
    id uuid NOT NULL,
    code character varying(32) DEFAULT ''::character varying NOT NULL,
    name character varying(200) NOT NULL,
    parent_id uuid,
    manager_employee_id uuid,
    is_accounting boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_device_tokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_device_tokens (
    token character varying(300) NOT NULL,
    username character varying(128) NOT NULL,
    platform character varying(20) DEFAULT 'android'::character varying NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_documents; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_documents (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    doc_type character varying(32) DEFAULT 'certificate'::character varying NOT NULL,
    title character varying(200) DEFAULT ''::character varying NOT NULL,
    issued_by character varying(200) DEFAULT ''::character varying NOT NULL,
    issued_date date,
    file_url text DEFAULT ''::text NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    doc_number character varying(120) DEFAULT ''::character varying NOT NULL,
    expires_at date,
    approval_status character varying(20) DEFAULT 'approved'::character varying NOT NULL,
    file_name character varying(260) DEFAULT ''::character varying NOT NULL,
    mime_type character varying(120) DEFAULT ''::character varying NOT NULL,
    file_content bytea
);

--
-- Name: hr_shift_assignments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_shift_assignments (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    shift_id uuid NOT NULL,
    work_date date NOT NULL,
    note text DEFAULT ''::text NOT NULL
);

--
-- Name: hr_shifts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_shifts (
    id uuid NOT NULL,
    code character varying(32) DEFAULT ''::character varying NOT NULL,
    name character varying(120) DEFAULT ''::character varying NOT NULL,
    start_time time without time zone DEFAULT '08:00:00'::time without time zone NOT NULL,
    end_time time without time zone DEFAULT '17:00:00'::time without time zone NOT NULL,
    break_minutes integer DEFAULT 60 NOT NULL,
    late_grace_minutes integer DEFAULT 5 NOT NULL,
    standard_hours numeric(5,2) DEFAULT 8 NOT NULL,
    is_overnight boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    checkout_grace_minutes integer DEFAULT 120 NOT NULL
);

--
-- Name: hr_effective_attendance_log; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.hr_effective_attendance_log AS
 WITH raw_mapped AS (
         SELECT l.id,
            l.username,
            l.full_name,
            l.loai,
            l.similarity,
            l.anh,
            l.occurred_at,
            l.ghi_chu,
            COALESCE(( SELECT a.work_date
                   FROM ((public.hr_employees e
                     JOIN public.hr_shift_assignments a ON ((a.employee_id = e.id)))
                     JOIN public.hr_shifts s ON (((s.id = a.shift_id) AND (s.is_overnight = true))))
                  WHERE ((lower((e.username)::text) = lower((l.username)::text)) AND ((l.loai)::text = 'Ra'::text) AND ((l.occurred_at AT TIME ZONE 'Asia/Ho_Chi_Minh'::text) >= (a.work_date + s.start_time)) AND ((l.occurred_at AT TIME ZONE 'Asia/Ho_Chi_Minh'::text) <= (((a.work_date + 1) + s.end_time) + make_interval(mins => s.checkout_grace_minutes))))
                  ORDER BY a.work_date DESC
                 LIMIT 1), ((l.occurred_at AT TIME ZONE 'Asia/Ho_Chi_Minh'::text))::date) AS logical_work_date
           FROM public.cham_cong_log l
          WHERE ((l.loai)::text = ANY ((ARRAY['Vào'::character varying, 'Ra'::character varying])::text[]))
        ), latest_correction AS (
         SELECT DISTINCT ON (c.employee_id, c.work_date, c.loai) c.id,
            c.request_id,
            c.employee_id,
            c.username,
            c.full_name,
            c.work_date,
            c.loai,
            c.occurred_at,
            c.reason,
            c.applied_at
           FROM public.hr_attendance_corrections c
          ORDER BY c.employee_id, c.work_date, c.loai, c.applied_at DESC, c.id DESC
        )
 SELECT r.id,
    r.username,
    r.full_name,
    r.loai,
    r.similarity,
    r.anh,
    r.occurred_at,
    r.ghi_chu,
    r.logical_work_date,
    false AS is_correction,
    NULL::uuid AS request_id
   FROM raw_mapped r
  WHERE (NOT (EXISTS ( SELECT 1
           FROM (latest_correction c
             JOIN public.hr_employees e ON ((e.id = c.employee_id)))
          WHERE ((lower((e.username)::text) = lower((r.username)::text)) AND (c.work_date = r.logical_work_date) AND ((c.loai)::text = (r.loai)::text)))))
UNION ALL
 SELECT (- c.id) AS id,
    e.username,
    e.full_name,
    c.loai,
    (0)::double precision AS similarity,
    NULL::text AS anh,
    c.occurred_at,
    c.reason AS ghi_chu,
    c.work_date AS logical_work_date,
    true AS is_correction,
    c.request_id
   FROM (latest_correction c
     JOIN public.hr_employees e ON ((e.id = c.employee_id)));

--
-- Name: hr_employee_benefits; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_employee_benefits (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    benefit_type character varying(40) NOT NULL,
    title character varying(200) NOT NULL,
    value_text text DEFAULT ''::text NOT NULL,
    valid_from date,
    valid_to date,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_employee_code_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.hr_employee_code_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: hr_employee_positions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_employee_positions (
    employee_id uuid NOT NULL,
    position_id uuid NOT NULL,
    is_primary boolean DEFAULT false NOT NULL,
    assigned_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    assigned_by character varying(128) DEFAULT ''::character varying NOT NULL
);

--
-- Name: hr_employee_rewards; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_employee_rewards (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    title character varying(200) NOT NULL,
    points integer DEFAULT 0 NOT NULL,
    awarded_at date DEFAULT CURRENT_DATE NOT NULL,
    note text DEFAULT ''::text NOT NULL
);

--
-- Name: hr_holidays; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_holidays (
    id uuid NOT NULL,
    holiday_date date NOT NULL,
    name character varying(160) DEFAULT ''::character varying NOT NULL,
    holiday_type character varying(24) DEFAULT 'company'::character varying NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    created_by character varying(100) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_job_positions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_job_positions (
    id uuid NOT NULL,
    code character varying(48) NOT NULL,
    name character varying(120) NOT NULL,
    default_role character varying(32) NOT NULL,
    default_access_role character varying(24) DEFAULT 'staff'::character varying NOT NULL,
    is_system boolean DEFAULT true NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    sort_order integer DEFAULT 100 NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT ck_hr_job_positions_access_role CHECK (((default_access_role)::text = ANY ((ARRAY['staff'::character varying, 'dept_manager'::character varying, 'location_manager'::character varying])::text[])))
);

--
-- Name: hr_leave_balances; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_leave_balances (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    year integer NOT NULL,
    leave_type character varying(32) DEFAULT 'annual'::character varying NOT NULL,
    total_days numeric(6,1) DEFAULT 0 NOT NULL,
    used_days numeric(6,1) DEFAULT 0 NOT NULL
);

--
-- Name: hr_locations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_locations (
    id uuid NOT NULL,
    code character varying(32) DEFAULT ''::character varying NOT NULL,
    name character varying(200) NOT NULL,
    address text DEFAULT ''::text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_onboarding_tasks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_onboarding_tasks (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    title character varying(200) NOT NULL,
    action_key character varying(40) DEFAULT ''::character varying NOT NULL,
    due_at timestamp with time zone,
    policy_text text DEFAULT ''::text NOT NULL,
    completed_at timestamp with time zone,
    acknowledged_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_payout_voucher_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_payout_voucher_events (
    id uuid NOT NULL,
    voucher_id uuid NOT NULL,
    action character varying(48) NOT NULL,
    actor_username character varying(128) DEFAULT ''::character varying NOT NULL,
    before_status character varying(32),
    after_status character varying(32),
    note text DEFAULT ''::text NOT NULL,
    before_data jsonb,
    after_data jsonb,
    occurred_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_payout_voucher_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.hr_payout_voucher_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: hr_payslip_history; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_payslip_history (
    id uuid NOT NULL,
    payslip_id uuid NOT NULL,
    employee_id uuid NOT NULL,
    employee_name character varying(200) DEFAULT ''::character varying NOT NULL,
    employee_code character varying(32) DEFAULT ''::character varying NOT NULL,
    period character varying(7) NOT NULL,
    revision integer NOT NULL,
    action character varying(32) NOT NULL,
    status_before character varying(24),
    status_after character varying(24) NOT NULL,
    actor character varying(128) NOT NULL,
    occurred_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    summary jsonb DEFAULT '{}'::jsonb NOT NULL,
    snapshot jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_hr_payslip_history_revision CHECK ((revision > 0))
);

--
-- Name: hr_payslip_inquiries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_payslip_inquiries (
    id uuid NOT NULL,
    payslip_id uuid NOT NULL,
    employee_id uuid NOT NULL,
    line_label character varying(200) DEFAULT ''::character varying NOT NULL,
    message text NOT NULL,
    status character varying(20) DEFAULT 'open'::character varying NOT NULL,
    response text DEFAULT ''::text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_payslips; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_payslips (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    period character varying(7) DEFAULT ''::character varying NOT NULL,
    work_days numeric(6,2) DEFAULT 0 NOT NULL,
    overtime_hours numeric(6,2) DEFAULT 0 NOT NULL,
    base_salary numeric(18,2) DEFAULT 0 NOT NULL,
    allowance numeric(18,2) DEFAULT 0 NOT NULL,
    overtime_pay numeric(18,2) DEFAULT 0 NOT NULL,
    deductions numeric(18,2) DEFAULT 0 NOT NULL,
    net_pay numeric(18,2) DEFAULT 0 NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    details jsonb DEFAULT '{}'::jsonb NOT NULL,
    published boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    acknowledged_at timestamp with time zone,
    created_by character varying(128) DEFAULT ''::character varying NOT NULL,
    updated_by character varying(128) DEFAULT ''::character varying NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    published_at timestamp with time zone
);

--
-- Name: hr_penalties; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_penalties (
    id uuid NOT NULL,
    penalty_no character varying(20) DEFAULT ''::character varying NOT NULL,
    employee_id uuid NOT NULL,
    penalty_type character varying(32) DEFAULT 'reminder'::character varying NOT NULL,
    penalty_date date DEFAULT CURRENT_DATE NOT NULL,
    amount numeric(18,2) DEFAULT 0 NOT NULL,
    installments integer DEFAULT 1 NOT NULL,
    start_period character varying(7) DEFAULT ''::character varying NOT NULL,
    reason text DEFAULT ''::text NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    status character varying(20) DEFAULT 'Active'::character varying NOT NULL,
    created_by character varying(128) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_penalty_ledger; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_penalty_ledger (
    penalty_id uuid NOT NULL,
    period character varying(7) NOT NULL,
    amount numeric(18,2) DEFAULT 0 NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_penalty_refund_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.hr_penalty_refund_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: hr_penalty_refunds; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_penalty_refunds (
    id uuid NOT NULL,
    refund_no character varying(20) DEFAULT ''::character varying NOT NULL,
    employee_id uuid NOT NULL,
    penalty_id uuid,
    penalty_no character varying(20) DEFAULT ''::character varying NOT NULL,
    appeal_request_no character varying(20) DEFAULT ''::character varying NOT NULL,
    amount numeric(18,2) DEFAULT 0 NOT NULL,
    reason text DEFAULT ''::text NOT NULL,
    status character varying(20) DEFAULT 'PendingAccounting'::character varying NOT NULL,
    payout_method character varying(16) DEFAULT ''::character varying NOT NULL,
    applied_period character varying(7) DEFAULT ''::character varying NOT NULL,
    created_by character varying(128) DEFAULT ''::character varying NOT NULL,
    approved_by character varying(128) DEFAULT ''::character varying NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    decided_at timestamp with time zone
);

--
-- Name: hr_penalty_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.hr_penalty_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: hr_performance_goals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_performance_goals (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    title character varying(200) NOT NULL,
    description text DEFAULT ''::text NOT NULL,
    target numeric(12,2) DEFAULT 100 NOT NULL,
    progress numeric(12,2) DEFAULT 0 NOT NULL,
    unit character varying(30) DEFAULT '%'::character varying NOT NULL,
    due_at timestamp with time zone,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_performance_reviews; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_performance_reviews (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    period character varying(30) NOT NULL,
    closes_at timestamp with time zone,
    self_assessment text DEFAULT ''::text NOT NULL,
    manager_comment text DEFAULT ''::text NOT NULL,
    score numeric(5,2),
    status character varying(20) DEFAULT 'open'::character varying NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_request_approvals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_request_approvals (
    id bigint NOT NULL,
    request_id uuid NOT NULL,
    step_no integer NOT NULL,
    approver_role character varying(32) DEFAULT ''::character varying NOT NULL,
    approver_username character varying(128) DEFAULT ''::character varying NOT NULL,
    approver_name character varying(200) DEFAULT ''::character varying NOT NULL,
    status character varying(20) DEFAULT 'Pending'::character varying NOT NULL,
    decided_at timestamp with time zone,
    decided_by character varying(128) DEFAULT ''::character varying NOT NULL,
    comment text DEFAULT ''::text NOT NULL,
    signature text
);

--
-- Name: hr_request_approvals_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.hr_request_approvals_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: hr_request_approvals_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.hr_request_approvals_id_seq OWNED BY public.hr_request_approvals.id;

--
-- Name: hr_request_attachments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_request_attachments (
    id bigint NOT NULL,
    request_id uuid NOT NULL,
    file_name character varying(260) NOT NULL,
    mime_type character varying(120) DEFAULT 'application/octet-stream'::character varying NOT NULL,
    file_size bigint NOT NULL,
    content bytea NOT NULL,
    uploaded_by character varying(128) NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_request_attachments_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.hr_request_attachments_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: hr_request_attachments_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.hr_request_attachments_id_seq OWNED BY public.hr_request_attachments.id;

--
-- Name: hr_request_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.hr_request_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: hr_requests; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_requests (
    id uuid NOT NULL,
    request_no character varying(20) DEFAULT ''::character varying NOT NULL,
    req_type character varying(32) NOT NULL,
    title character varying(200) DEFAULT ''::character varying NOT NULL,
    employee_id uuid NOT NULL,
    requester_username character varying(128) DEFAULT ''::character varying NOT NULL,
    payload jsonb DEFAULT '{}'::jsonb NOT NULL,
    status character varying(20) DEFAULT 'Pending'::character varying NOT NULL,
    current_step integer DEFAULT 1 NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    due_at timestamp with time zone,
    last_reminded_at timestamp with time zone
);

--
-- Name: hr_salaries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_salaries (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    base_salary numeric(18,2) DEFAULT 0 NOT NULL,
    allowance numeric(18,2) DEFAULT 0 NOT NULL,
    overtime_rate numeric(18,2) DEFAULT 0 NOT NULL,
    components jsonb DEFAULT '[]'::jsonb NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    updated_by character varying(128) DEFAULT ''::character varying NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_salary_raises; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_salary_raises (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    contract_id uuid,
    effective_period character varying(7) NOT NULL,
    amount numeric(18,2) DEFAULT 0 NOT NULL,
    decision_no character varying(64) DEFAULT ''::character varying NOT NULL,
    reason text DEFAULT ''::text NOT NULL,
    created_by character varying(128) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_training_courses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_training_courses (
    id uuid NOT NULL,
    title character varying(200) NOT NULL,
    description text DEFAULT ''::text NOT NULL,
    material_url text DEFAULT ''::text NOT NULL,
    video_url text DEFAULT ''::text NOT NULL,
    quiz jsonb DEFAULT '[]'::jsonb NOT NULL,
    certificate_valid_months integer,
    active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: hr_training_enrollments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hr_training_enrollments (
    course_id uuid NOT NULL,
    employee_id uuid NOT NULL,
    progress integer DEFAULT 0 NOT NULL,
    resume_seconds integer DEFAULT 0 NOT NULL,
    score numeric(5,2),
    completed_at timestamp with time zone,
    certificate_expires_at date,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: password_recovery_codes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.password_recovery_codes (
    id bigint NOT NULL,
    username character varying(128) NOT NULL,
    code_hash text NOT NULL,
    created_by character varying(128) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    expires_at timestamp with time zone,
    used_at timestamp with time zone
);

--
-- Name: password_recovery_codes_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.password_recovery_codes_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: password_recovery_codes_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.password_recovery_codes_id_seq OWNED BY public.password_recovery_codes.id;

--
-- Name: password_reset_requests; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.password_reset_requests (
    id bigint NOT NULL,
    username character varying(128) DEFAULT ''::character varying NOT NULL,
    resolved_by character varying(128) DEFAULT ''::character varying NOT NULL
);

--
-- Name: password_reset_requests_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.password_reset_requests_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: password_reset_requests_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.password_reset_requests_id_seq OWNED BY public.password_reset_requests.id;

--
-- Name: payments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payments (
    id uuid NOT NULL,
    customer_id uuid,
    customer_name character varying(256) DEFAULT ''::character varying NOT NULL,
    customer_input_name character varying(256) DEFAULT ''::character varying NOT NULL,
    amount numeric(18,2) DEFAULT 0 NOT NULL,
    pay_date date DEFAULT CURRENT_DATE NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    source_kind character varying(32) DEFAULT ''::character varying NOT NULL,
    source_id uuid,
    created_by character varying(128) DEFAULT ''::character varying NOT NULL
);

--
-- Name: product_code_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.product_code_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: products; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.products (
    id uuid NOT NULL,
    code character varying(32) DEFAULT ''::character varying NOT NULL,
    name character varying(256) DEFAULT ''::character varying NOT NULL,
    spec character varying(256) DEFAULT ''::character varying NOT NULL,
    unit character varying(24) DEFAULT 'kg'::character varying NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: purchase_lines; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.purchase_lines (
    id bigint NOT NULL,
    purchase_id uuid NOT NULL,
    line_no integer DEFAULT 0 NOT NULL,
    product_id uuid,
    line_content text DEFAULT ''::text NOT NULL,
    spec text DEFAULT ''::text NOT NULL,
    quantity numeric(18,2) DEFAULT 0 NOT NULL,
    unit_price numeric(18,2) DEFAULT 0 NOT NULL,
    note text DEFAULT ''::text NOT NULL
);

--
-- Name: purchase_lines_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.purchase_lines_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: purchase_lines_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.purchase_lines_id_seq OWNED BY public.purchase_lines.id;

--
-- Name: purchase_voucher_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.purchase_voucher_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: purchases; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.purchases (
    id uuid NOT NULL,
    voucher_no character varying(64) DEFAULT ''::character varying NOT NULL,
    doc_date date NOT NULL,
    supplier_id uuid,
    supplier_name character varying(256) DEFAULT ''::character varying NOT NULL,
    supplier_invoice_no character varying(64) DEFAULT ''::character varying NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    paid_amount numeric(18,2) DEFAULT 0 NOT NULL,
    cancelled_at timestamp with time zone,
    cancelled_by character varying(128) DEFAULT ''::character varying NOT NULL,
    cancel_reason text DEFAULT ''::text NOT NULL,
    created_by character varying(128) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: registration_codes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.registration_codes (
    id bigint NOT NULL,
    created_by character varying(128) DEFAULT ''::character varying NOT NULL,
    used_by character varying(128) DEFAULT ''::character varying NOT NULL
);

--
-- Name: registration_codes_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.registration_codes_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: registration_codes_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.registration_codes_id_seq OWNED BY public.registration_codes.id;

--
-- Name: schema_migrations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.schema_migrations (
    version character varying(64) NOT NULL,
    description text NOT NULL,
    applied_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: suppliers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.suppliers (
    id uuid NOT NULL,
    name character varying(256) NOT NULL,
    tax_code character varying(64) DEFAULT ''::character varying NOT NULL,
    phone character varying(64) DEFAULT ''::character varying NOT NULL,
    address text DEFAULT ''::text NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: survey_answers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.survey_answers (
    id bigint NOT NULL,
    response_id uuid NOT NULL,
    question_id uuid NOT NULL,
    answer text DEFAULT ''::text NOT NULL,
    option_indices jsonb DEFAULT '[]'::jsonb NOT NULL
);

--
-- Name: survey_answers_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.survey_answers_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: survey_answers_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.survey_answers_id_seq OWNED BY public.survey_answers.id;

--
-- Name: survey_questions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.survey_questions (
    id uuid NOT NULL,
    survey_id uuid NOT NULL,
    question text DEFAULT ''::text NOT NULL,
    qtype character varying(16) DEFAULT 'single'::character varying NOT NULL,
    options jsonb DEFAULT '[]'::jsonb NOT NULL,
    order_no integer DEFAULT 0 NOT NULL,
    required boolean DEFAULT true NOT NULL
);

--
-- Name: survey_responses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.survey_responses (
    id uuid NOT NULL,
    survey_id uuid NOT NULL,
    respondent_hash character varying(64),
    submitted_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: surveys; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.surveys (
    id uuid NOT NULL,
    title character varying(300) DEFAULT ''::character varying NOT NULL,
    description text DEFAULT ''::text NOT NULL,
    is_anonymous boolean DEFAULT true NOT NULL,
    allow_multiple boolean DEFAULT false NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_by character varying(128) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    closes_at timestamp with time zone
);

--
-- Name: system_roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.system_roles (
    code character varying(32) NOT NULL,
    name character varying(120) NOT NULL,
    is_assignable boolean DEFAULT true NOT NULL,
    is_technical boolean DEFAULT false NOT NULL,
    sort_order integer DEFAULT 100 NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: user_role_history; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_role_history (
    id bigint NOT NULL,
    occurred_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    username character varying(128) NOT NULL,
    changed_by character varying(128) DEFAULT ''::character varying NOT NULL,
    action character varying(64) NOT NULL,
    roles_before text DEFAULT ''::text NOT NULL,
    roles_after text DEFAULT ''::text NOT NULL,
    reason text DEFAULT ''::text NOT NULL,
    client_ip character varying(64) DEFAULT ''::character varying NOT NULL
);

--
-- Name: user_role_history_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.user_role_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: user_role_history_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.user_role_history_id_seq OWNED BY public.user_role_history.id;

--
-- Name: user_roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_roles (
    username character varying(128) NOT NULL,
    role character varying(32) NOT NULL,
    granted_by character varying(128) DEFAULT ''::character varying NOT NULL,
    granted_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    expires_at timestamp with time zone
);

--
-- Name: user_sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_sessions (
    session_token character varying(128) NOT NULL,
    username character varying(128) DEFAULT ''::character varying NOT NULL,
    machine_name character varying(128) DEFAULT ''::character varying NOT NULL,
    started_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    last_seen timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    ended_at timestamp with time zone,
    is_active boolean DEFAULT true NOT NULL,
    end_reason text DEFAULT ''::text NOT NULL,
    client_kind character varying(20) DEFAULT 'Desktop'::character varying NOT NULL,
    user_agent text DEFAULT ''::text NOT NULL,
    revoked boolean DEFAULT false NOT NULL,
    revoked_at timestamp with time zone,
    revoked_by character varying(128) DEFAULT ''::character varying NOT NULL
);

--
-- Name: web_diamond_members; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.web_diamond_members (
    username character varying(128) NOT NULL,
    granted_by character varying(128) DEFAULT ''::character varying NOT NULL,
    granted_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: web_login_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.web_login_settings (
    username character varying(150) NOT NULL,
    web_login_enabled boolean DEFAULT true NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: web_notifications; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.web_notifications (
    id bigint NOT NULL,
    username character varying(128) NOT NULL,
    title character varying(200) NOT NULL,
    body text DEFAULT ''::text NOT NULL,
    category character varying(40) DEFAULT 'general'::character varying NOT NULL,
    link character varying(300) DEFAULT ''::character varying NOT NULL,
    notif_id character varying(200) DEFAULT ''::character varying NOT NULL,
    actor character varying(128) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    read_at timestamp with time zone,
    app_target character varying(60) DEFAULT ''::character varying NOT NULL
);

--
-- Name: web_notifications_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.web_notifications_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: web_notifications_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.web_notifications_id_seq OWNED BY public.web_notifications.id;

--
-- Name: web_system_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.web_system_settings (
    setting_key character varying(120) NOT NULL,
    setting_value text DEFAULT ''::text NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_by character varying(100) DEFAULT ''::character varying NOT NULL
);

--
-- Name: web_user_avatars; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.web_user_avatars (
    user_id uuid NOT NULL,
    image_data_url text NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: web_user_preferences; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.web_user_preferences (
    user_id uuid NOT NULL,
    preference_key character varying(120) NOT NULL,
    preference_value text DEFAULT ''::text NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: web_verified_users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.web_verified_users (
    username character varying(128) NOT NULL,
    granted_by character varying(128) DEFAULT ''::character varying NOT NULL,
    granted_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: work_access_requests; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.work_access_requests (
    id bigint NOT NULL,
    username character varying(128) DEFAULT ''::character varying NOT NULL,
    approved_by character varying(128) DEFAULT ''::character varying NOT NULL
);

--
-- Name: work_access_requests_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.work_access_requests_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: work_access_requests_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.work_access_requests_id_seq OWNED BY public.work_access_requests.id;

--
-- Name: work_task_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.work_task_events (
    id bigint NOT NULL,
    task_id uuid NOT NULL,
    actor_username character varying(128) DEFAULT ''::character varying NOT NULL,
    actor_name character varying(200) DEFAULT ''::character varying NOT NULL,
    kind character varying(20) DEFAULT 'comment'::character varying NOT NULL,
    note text DEFAULT ''::text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: work_task_events_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.work_task_events_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: work_task_events_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.work_task_events_id_seq OWNED BY public.work_task_events.id;

--
-- Name: work_task_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.work_task_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: work_tasks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.work_tasks (
    id uuid NOT NULL,
    task_no character varying(24) DEFAULT ''::character varying NOT NULL,
    title character varying(300) DEFAULT ''::character varying NOT NULL,
    description text DEFAULT ''::text NOT NULL,
    assigner_username character varying(128) DEFAULT ''::character varying NOT NULL,
    assigner_name character varying(200) DEFAULT ''::character varying NOT NULL,
    assignee_username character varying(128) DEFAULT ''::character varying NOT NULL,
    assignee_name character varying(200) DEFAULT ''::character varying NOT NULL,
    priority character varying(16) DEFAULT 'normal'::character varying NOT NULL,
    due_at timestamp with time zone,
    status character varying(20) DEFAULT 'assigned'::character varying NOT NULL,
    progress integer DEFAULT 0 NOT NULL,
    submit_note text DEFAULT ''::text NOT NULL,
    submitted_at timestamp with time zone,
    review_note text DEFAULT ''::text NOT NULL,
    rating integer,
    reviewed_at timestamp with time zone,
    reviewed_by character varying(128) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    source_kind character varying(24) DEFAULT ''::character varying NOT NULL,
    source_document_id uuid
);

--
-- Name: app_feedbacks id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_feedbacks ALTER COLUMN id SET DEFAULT nextval('public.app_feedbacks_id_seq'::regclass);

--
-- Name: app_outbox id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_outbox ALTER COLUMN id SET DEFAULT nextval('public.app_outbox_id_seq'::regclass);

--
-- Name: app_portal_posts id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_portal_posts ALTER COLUMN id SET DEFAULT nextval('public.app_portal_posts_id_seq'::regclass);

--
-- Name: app_releases id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_releases ALTER COLUMN id SET DEFAULT nextval('public.app_releases_id_seq'::regclass);

--
-- Name: audit_logs id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs ALTER COLUMN id SET DEFAULT nextval('public.audit_logs_id_seq'::regclass);

--
-- Name: cash_count_lines id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_count_lines ALTER COLUMN id SET DEFAULT nextval('public.cash_count_lines_id_seq'::regclass);

--
-- Name: cham_cong_face id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cham_cong_face ALTER COLUMN id SET DEFAULT nextval('public.cham_cong_face_id_seq'::regclass);

--
-- Name: cham_cong_face_enrollment_samples id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cham_cong_face_enrollment_samples ALTER COLUMN id SET DEFAULT nextval('public.cham_cong_face_enrollment_samples_id_seq'::regclass);

--
-- Name: cham_cong_log id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cham_cong_log ALTER COLUMN id SET DEFAULT nextval('public.cham_cong_log_id_seq'::regclass);

--
-- Name: cham_cong_offline id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cham_cong_offline ALTER COLUMN id SET DEFAULT nextval('public.cham_cong_offline_id_seq'::regclass);

--
-- Name: customer_aliases id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_aliases ALTER COLUMN id SET DEFAULT nextval('public.customer_aliases_id_seq'::regclass);

--
-- Name: document_line_edits id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.document_line_edits ALTER COLUMN id SET DEFAULT nextval('public.document_line_edits_id_seq'::regclass);

--
-- Name: document_lines id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.document_lines ALTER COLUMN id SET DEFAULT nextval('public.document_lines_id_seq'::regclass);

--
-- Name: gia_cong_hang_hoa id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gia_cong_hang_hoa ALTER COLUMN id SET DEFAULT nextval('public.gia_cong_hang_hoa_id_seq'::regclass);

--
-- Name: gia_cong_phieu id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gia_cong_phieu ALTER COLUMN id SET DEFAULT nextval('public.gia_cong_phieu_id_seq'::regclass);

--
-- Name: hr_attendance_corrections id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_attendance_corrections ALTER COLUMN id SET DEFAULT nextval('public.hr_attendance_corrections_id_seq'::regclass);

--
-- Name: hr_request_approvals id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_request_approvals ALTER COLUMN id SET DEFAULT nextval('public.hr_request_approvals_id_seq'::regclass);

--
-- Name: hr_request_attachments id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_request_attachments ALTER COLUMN id SET DEFAULT nextval('public.hr_request_attachments_id_seq'::regclass);

--
-- Name: password_recovery_codes id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.password_recovery_codes ALTER COLUMN id SET DEFAULT nextval('public.password_recovery_codes_id_seq'::regclass);

--
-- Name: password_reset_requests id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.password_reset_requests ALTER COLUMN id SET DEFAULT nextval('public.password_reset_requests_id_seq'::regclass);

--
-- Name: purchase_lines id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_lines ALTER COLUMN id SET DEFAULT nextval('public.purchase_lines_id_seq'::regclass);

--
-- Name: registration_codes id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.registration_codes ALTER COLUMN id SET DEFAULT nextval('public.registration_codes_id_seq'::regclass);

--
-- Name: survey_answers id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.survey_answers ALTER COLUMN id SET DEFAULT nextval('public.survey_answers_id_seq'::regclass);

--
-- Name: user_role_history id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_role_history ALTER COLUMN id SET DEFAULT nextval('public.user_role_history_id_seq'::regclass);

--
-- Name: web_notifications id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_notifications ALTER COLUMN id SET DEFAULT nextval('public.web_notifications_id_seq'::regclass);

--
-- Name: work_access_requests id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.work_access_requests ALTER COLUMN id SET DEFAULT nextval('public.work_access_requests_id_seq'::regclass);

--
-- Name: work_task_events id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.work_task_events ALTER COLUMN id SET DEFAULT nextval('public.work_task_events_id_seq'::regclass);

--
-- Name: app_config app_config_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_config
    ADD CONSTRAINT app_config_pkey PRIMARY KEY (id);

--
-- Name: app_feedbacks app_feedbacks_legacy_chat_report_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_feedbacks
    ADD CONSTRAINT app_feedbacks_legacy_chat_report_id_key UNIQUE (legacy_chat_report_id);

--
-- Name: app_feedbacks app_feedbacks_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_feedbacks
    ADD CONSTRAINT app_feedbacks_pkey PRIMARY KEY (id);

--
-- Name: app_general_feedback app_general_feedback_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_general_feedback
    ADD CONSTRAINT app_general_feedback_pkey PRIMARY KEY (id);

--
-- Name: app_outbox app_outbox_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_outbox
    ADD CONSTRAINT app_outbox_pkey PRIMARY KEY (id);

--
-- Name: app_pin_codes app_pin_codes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_pin_codes
    ADD CONSTRAINT app_pin_codes_pkey PRIMARY KEY (username);

--
-- Name: app_portal_about app_portal_about_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_portal_about
    ADD CONSTRAINT app_portal_about_pkey PRIMARY KEY (id);

--
-- Name: app_portal_posts app_portal_posts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_portal_posts
    ADD CONSTRAINT app_portal_posts_pkey PRIMARY KEY (id);

--
-- Name: app_releases app_releases_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_releases
    ADD CONSTRAINT app_releases_pkey PRIMARY KEY (id);

--
-- Name: app_settings app_settings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_settings
    ADD CONSTRAINT app_settings_pkey PRIMARY KEY (setting_key);

--
-- Name: app_support_tickets app_support_tickets_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_support_tickets
    ADD CONSTRAINT app_support_tickets_pkey PRIMARY KEY (id);

--
-- Name: app_support_tickets app_support_tickets_ticket_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_support_tickets
    ADD CONSTRAINT app_support_tickets_ticket_code_key UNIQUE (ticket_code);

--
-- Name: app_survey_responses app_survey_responses_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_survey_responses
    ADD CONSTRAINT app_survey_responses_pkey PRIMARY KEY (id);

--
-- Name: app_survey_responses app_survey_responses_survey_id_username_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_survey_responses
    ADD CONSTRAINT app_survey_responses_survey_id_username_key UNIQUE (survey_id, username);

--
-- Name: app_surveys app_surveys_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_surveys
    ADD CONSTRAINT app_surveys_pkey PRIMARY KEY (id);

--
-- Name: app_users app_users_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_users
    ADD CONSTRAINT app_users_pkey PRIMARY KEY (id);

--
-- Name: app_users app_users_username_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_users
    ADD CONSTRAINT app_users_username_key UNIQUE (username);

--
-- Name: audit_logs audit_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs
    ADD CONSTRAINT audit_logs_pkey PRIMARY KEY (id);

--
-- Name: cash_collection_events cash_collection_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_collection_events
    ADD CONSTRAINT cash_collection_events_pkey PRIMARY KEY (id);

--
-- Name: cash_collection_orders cash_collection_orders_order_no_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_collection_orders
    ADD CONSTRAINT cash_collection_orders_order_no_key UNIQUE (order_no);

--
-- Name: cash_collection_orders cash_collection_orders_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_collection_orders
    ADD CONSTRAINT cash_collection_orders_pkey PRIMARY KEY (id);

--
-- Name: cash_count_lines cash_count_lines_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_count_lines
    ADD CONSTRAINT cash_count_lines_pkey PRIMARY KEY (id);

--
-- Name: cash_count_lines cash_count_lines_session_id_denomination_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_count_lines
    ADD CONSTRAINT cash_count_lines_session_id_denomination_key UNIQUE (session_id, denomination);

--
-- Name: cash_count_sessions cash_count_sessions_order_id_stage_revision_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_count_sessions
    ADD CONSTRAINT cash_count_sessions_order_id_stage_revision_key UNIQUE (order_id, stage, revision);

--
-- Name: cash_count_sessions cash_count_sessions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_count_sessions
    ADD CONSTRAINT cash_count_sessions_pkey PRIMARY KEY (id);

--
-- Name: cash_fund_manual_entries cash_fund_manual_entries_entry_no_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_fund_manual_entries
    ADD CONSTRAINT cash_fund_manual_entries_entry_no_key UNIQUE (entry_no);

--
-- Name: cash_fund_manual_entries cash_fund_manual_entries_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_fund_manual_entries
    ADD CONSTRAINT cash_fund_manual_entries_pkey PRIMARY KEY (id);

--
-- Name: cham_cong_face_enrollment_samples cham_cong_face_enrollment_samples_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cham_cong_face_enrollment_samples
    ADD CONSTRAINT cham_cong_face_enrollment_samples_pkey PRIMARY KEY (id);

--
-- Name: cham_cong_face_enrollment_samples cham_cong_face_enrollment_samples_request_id_pose_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cham_cong_face_enrollment_samples
    ADD CONSTRAINT cham_cong_face_enrollment_samples_request_id_pose_key UNIQUE (request_id, pose);

--
-- Name: cham_cong_face_enrollments cham_cong_face_enrollments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cham_cong_face_enrollments
    ADD CONSTRAINT cham_cong_face_enrollments_pkey PRIMARY KEY (id);

--
-- Name: cham_cong_face cham_cong_face_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cham_cong_face
    ADD CONSTRAINT cham_cong_face_pkey PRIMARY KEY (id);

--
-- Name: cham_cong_log cham_cong_log_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cham_cong_log
    ADD CONSTRAINT cham_cong_log_pkey PRIMARY KEY (id);

--
-- Name: cham_cong_offline cham_cong_offline_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cham_cong_offline
    ADD CONSTRAINT cham_cong_offline_pkey PRIMARY KEY (id);

--
-- Name: cham_cong_qr_sites cham_cong_qr_sites_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cham_cong_qr_sites
    ADD CONSTRAINT cham_cong_qr_sites_pkey PRIMARY KEY (id);

--
-- Name: cham_cong_qr_sites cham_cong_qr_sites_qr_token_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cham_cong_qr_sites
    ADD CONSTRAINT cham_cong_qr_sites_qr_token_key UNIQUE (qr_token);

--
-- Name: customer_aliases customer_aliases_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_aliases
    ADD CONSTRAINT customer_aliases_pkey PRIMARY KEY (id);

--
-- Name: customer_opening_balances customer_opening_balances_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_opening_balances
    ADD CONSTRAINT customer_opening_balances_pkey PRIMARY KEY (customer_id);

--
-- Name: customers customers_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customers
    ADD CONSTRAINT customers_pkey PRIMARY KEY (id);

--
-- Name: document_issued_lines document_issued_lines_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.document_issued_lines
    ADD CONSTRAINT document_issued_lines_pkey PRIMARY KEY (document_id, line_no);

--
-- Name: document_line_edits document_line_edits_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.document_line_edits
    ADD CONSTRAINT document_line_edits_pkey PRIMARY KEY (id);

--
-- Name: document_lines document_lines_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.document_lines
    ADD CONSTRAINT document_lines_pkey PRIMARY KEY (id);

--
-- Name: documents documents_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.documents
    ADD CONSTRAINT documents_pkey PRIMARY KEY (id);

--
-- Name: gia_cong_hang_hoa gia_cong_hang_hoa_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gia_cong_hang_hoa
    ADD CONSTRAINT gia_cong_hang_hoa_pkey PRIMARY KEY (id);

--
-- Name: gia_cong_phieu gia_cong_phieu_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gia_cong_phieu
    ADD CONSTRAINT gia_cong_phieu_pkey PRIMARY KEY (id);

--
-- Name: help_faqs help_faqs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.help_faqs
    ADD CONSTRAINT help_faqs_pkey PRIMARY KEY (id);

--
-- Name: hr_anniversary_letter hr_anniversary_letter_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_anniversary_letter
    ADD CONSTRAINT hr_anniversary_letter_pkey PRIMARY KEY (id);

--
-- Name: hr_approval_delegations hr_approval_delegations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_approval_delegations
    ADD CONSTRAINT hr_approval_delegations_pkey PRIMARY KEY (from_username);

--
-- Name: hr_attendance_corrections hr_attendance_corrections_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_attendance_corrections
    ADD CONSTRAINT hr_attendance_corrections_pkey PRIMARY KEY (id);

--
-- Name: hr_attendance_corrections hr_attendance_corrections_request_id_loai_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_attendance_corrections
    ADD CONSTRAINT hr_attendance_corrections_request_id_loai_key UNIQUE (request_id, loai);

--
-- Name: hr_attendance_reminders hr_attendance_reminders_employee_id_work_date_direction_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_attendance_reminders
    ADD CONSTRAINT hr_attendance_reminders_employee_id_work_date_direction_key UNIQUE (employee_id, work_date, direction);

--
-- Name: hr_attendance_reminders hr_attendance_reminders_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_attendance_reminders
    ADD CONSTRAINT hr_attendance_reminders_pkey PRIMARY KEY (id);

--
-- Name: hr_bank_accounts hr_bank_accounts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_bank_accounts
    ADD CONSTRAINT hr_bank_accounts_pkey PRIMARY KEY (id);

--
-- Name: hr_contracts hr_contracts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_contracts
    ADD CONSTRAINT hr_contracts_pkey PRIMARY KEY (id);

--
-- Name: hr_departments hr_departments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_departments
    ADD CONSTRAINT hr_departments_pkey PRIMARY KEY (id);

--
-- Name: hr_device_tokens hr_device_tokens_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_device_tokens
    ADD CONSTRAINT hr_device_tokens_pkey PRIMARY KEY (token);

--
-- Name: hr_documents hr_documents_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_documents
    ADD CONSTRAINT hr_documents_pkey PRIMARY KEY (id);

--
-- Name: hr_employee_benefits hr_employee_benefits_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_employee_benefits
    ADD CONSTRAINT hr_employee_benefits_pkey PRIMARY KEY (id);

--
-- Name: hr_employee_positions hr_employee_positions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_employee_positions
    ADD CONSTRAINT hr_employee_positions_pkey PRIMARY KEY (employee_id, position_id);

--
-- Name: hr_employee_rewards hr_employee_rewards_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_employee_rewards
    ADD CONSTRAINT hr_employee_rewards_pkey PRIMARY KEY (id);

--
-- Name: hr_employees hr_employees_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_employees
    ADD CONSTRAINT hr_employees_pkey PRIMARY KEY (id);

--
-- Name: hr_holidays hr_holidays_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_holidays
    ADD CONSTRAINT hr_holidays_pkey PRIMARY KEY (id);

--
-- Name: hr_job_positions hr_job_positions_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_job_positions
    ADD CONSTRAINT hr_job_positions_code_key UNIQUE (code);

--
-- Name: hr_job_positions hr_job_positions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_job_positions
    ADD CONSTRAINT hr_job_positions_pkey PRIMARY KEY (id);

--
-- Name: hr_leave_balances hr_leave_balances_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_leave_balances
    ADD CONSTRAINT hr_leave_balances_pkey PRIMARY KEY (id);

--
-- Name: hr_locations hr_locations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_locations
    ADD CONSTRAINT hr_locations_pkey PRIMARY KEY (id);

--
-- Name: hr_onboarding_tasks hr_onboarding_tasks_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_onboarding_tasks
    ADD CONSTRAINT hr_onboarding_tasks_pkey PRIMARY KEY (id);

--
-- Name: hr_payout_categories hr_payout_categories_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_payout_categories
    ADD CONSTRAINT hr_payout_categories_code_key UNIQUE (code);

--
-- Name: hr_payout_categories hr_payout_categories_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_payout_categories
    ADD CONSTRAINT hr_payout_categories_pkey PRIMARY KEY (id);

--
-- Name: hr_payout_voucher_events hr_payout_voucher_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_payout_voucher_events
    ADD CONSTRAINT hr_payout_voucher_events_pkey PRIMARY KEY (id);

--
-- Name: hr_payout_vouchers hr_payout_vouchers_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_payout_vouchers
    ADD CONSTRAINT hr_payout_vouchers_pkey PRIMARY KEY (id);

--
-- Name: hr_payslip_history hr_payslip_history_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_payslip_history
    ADD CONSTRAINT hr_payslip_history_pkey PRIMARY KEY (id);

--
-- Name: hr_payslip_inquiries hr_payslip_inquiries_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_payslip_inquiries
    ADD CONSTRAINT hr_payslip_inquiries_pkey PRIMARY KEY (id);

--
-- Name: hr_payslips hr_payslips_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_payslips
    ADD CONSTRAINT hr_payslips_pkey PRIMARY KEY (id);

--
-- Name: hr_penalties hr_penalties_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_penalties
    ADD CONSTRAINT hr_penalties_pkey PRIMARY KEY (id);

--
-- Name: hr_penalty_ledger hr_penalty_ledger_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_penalty_ledger
    ADD CONSTRAINT hr_penalty_ledger_pkey PRIMARY KEY (penalty_id, period);

--
-- Name: hr_penalty_refunds hr_penalty_refunds_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_penalty_refunds
    ADD CONSTRAINT hr_penalty_refunds_pkey PRIMARY KEY (id);

--
-- Name: hr_performance_goals hr_performance_goals_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_performance_goals
    ADD CONSTRAINT hr_performance_goals_pkey PRIMARY KEY (id);

--
-- Name: hr_performance_reviews hr_performance_reviews_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_performance_reviews
    ADD CONSTRAINT hr_performance_reviews_pkey PRIMARY KEY (id);

--
-- Name: hr_request_approvals hr_request_approvals_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_request_approvals
    ADD CONSTRAINT hr_request_approvals_pkey PRIMARY KEY (id);

--
-- Name: hr_request_attachments hr_request_attachments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_request_attachments
    ADD CONSTRAINT hr_request_attachments_pkey PRIMARY KEY (id);

--
-- Name: hr_requests hr_requests_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_requests
    ADD CONSTRAINT hr_requests_pkey PRIMARY KEY (id);

--
-- Name: hr_salaries hr_salaries_employee_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_salaries
    ADD CONSTRAINT hr_salaries_employee_id_key UNIQUE (employee_id);

--
-- Name: hr_salaries hr_salaries_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_salaries
    ADD CONSTRAINT hr_salaries_pkey PRIMARY KEY (id);

--
-- Name: hr_salary_raises hr_salary_raises_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_salary_raises
    ADD CONSTRAINT hr_salary_raises_pkey PRIMARY KEY (id);

--
-- Name: hr_shift_assignments hr_shift_assignments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_shift_assignments
    ADD CONSTRAINT hr_shift_assignments_pkey PRIMARY KEY (id);

--
-- Name: hr_shifts hr_shifts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_shifts
    ADD CONSTRAINT hr_shifts_pkey PRIMARY KEY (id);

--
-- Name: hr_training_courses hr_training_courses_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_training_courses
    ADD CONSTRAINT hr_training_courses_pkey PRIMARY KEY (id);

--
-- Name: hr_training_enrollments hr_training_enrollments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_training_enrollments
    ADD CONSTRAINT hr_training_enrollments_pkey PRIMARY KEY (course_id, employee_id);

--
-- Name: password_recovery_codes password_recovery_codes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.password_recovery_codes
    ADD CONSTRAINT password_recovery_codes_pkey PRIMARY KEY (id);

--
-- Name: password_reset_requests password_reset_requests_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.password_reset_requests
    ADD CONSTRAINT password_reset_requests_pkey PRIMARY KEY (id);

--
-- Name: payments payments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_pkey PRIMARY KEY (id);

--
-- Name: web_user_preferences pk_web_user_preferences; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_user_preferences
    ADD CONSTRAINT pk_web_user_preferences PRIMARY KEY (user_id, preference_key);

--
-- Name: products products_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_pkey PRIMARY KEY (id);

--
-- Name: purchase_lines purchase_lines_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_lines
    ADD CONSTRAINT purchase_lines_pkey PRIMARY KEY (id);

--
-- Name: purchases purchases_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchases
    ADD CONSTRAINT purchases_pkey PRIMARY KEY (id);

--
-- Name: registration_codes registration_codes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.registration_codes
    ADD CONSTRAINT registration_codes_pkey PRIMARY KEY (id);

--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);

--
-- Name: suppliers suppliers_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.suppliers
    ADD CONSTRAINT suppliers_pkey PRIMARY KEY (id);

--
-- Name: survey_answers survey_answers_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.survey_answers
    ADD CONSTRAINT survey_answers_pkey PRIMARY KEY (id);

--
-- Name: survey_questions survey_questions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.survey_questions
    ADD CONSTRAINT survey_questions_pkey PRIMARY KEY (id);

--
-- Name: survey_responses survey_responses_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.survey_responses
    ADD CONSTRAINT survey_responses_pkey PRIMARY KEY (id);

--
-- Name: surveys surveys_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.surveys
    ADD CONSTRAINT surveys_pkey PRIMARY KEY (id);

--
-- Name: system_roles system_roles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.system_roles
    ADD CONSTRAINT system_roles_pkey PRIMARY KEY (code);

--
-- Name: user_role_history user_role_history_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_role_history
    ADD CONSTRAINT user_role_history_pkey PRIMARY KEY (id);

--
-- Name: user_roles user_roles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_roles
    ADD CONSTRAINT user_roles_pkey PRIMARY KEY (username, role);

--
-- Name: user_sessions user_sessions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_sessions
    ADD CONSTRAINT user_sessions_pkey PRIMARY KEY (session_token);

--
-- Name: hr_payslip_history ux_hr_payslip_history_revision; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_payslip_history
    ADD CONSTRAINT ux_hr_payslip_history_revision UNIQUE (payslip_id, revision);

--
-- Name: web_diamond_members web_diamond_members_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_diamond_members
    ADD CONSTRAINT web_diamond_members_pkey PRIMARY KEY (username);

--
-- Name: web_login_settings web_login_settings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_login_settings
    ADD CONSTRAINT web_login_settings_pkey PRIMARY KEY (username);

--
-- Name: web_notifications web_notifications_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_notifications
    ADD CONSTRAINT web_notifications_pkey PRIMARY KEY (id);

--
-- Name: web_system_settings web_system_settings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_system_settings
    ADD CONSTRAINT web_system_settings_pkey PRIMARY KEY (setting_key);

--
-- Name: web_user_avatars web_user_avatars_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_user_avatars
    ADD CONSTRAINT web_user_avatars_pkey PRIMARY KEY (user_id);

--
-- Name: web_verified_users web_verified_users_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_verified_users
    ADD CONSTRAINT web_verified_users_pkey PRIMARY KEY (username);

--
-- Name: work_access_requests work_access_requests_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.work_access_requests
    ADD CONSTRAINT work_access_requests_pkey PRIMARY KEY (id);

--
-- Name: work_task_events work_task_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.work_task_events
    ADD CONSTRAINT work_task_events_pkey PRIMARY KEY (id);

--
-- Name: work_tasks work_tasks_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.work_tasks
    ADD CONSTRAINT work_tasks_pkey PRIMARY KEY (id);

--
-- Name: ix_app_feedbacks_reporter; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_app_feedbacks_reporter ON public.app_feedbacks USING btree (reporter_username, created_at DESC);

--
-- Name: ix_app_feedbacks_type_created; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_app_feedbacks_type_created ON public.app_feedbacks USING btree (feedback_type, created_at DESC);

--
-- Name: ix_app_outbox_ready; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_app_outbox_ready ON public.app_outbox USING btree (available_at, id) WHERE ((status)::text = 'pending'::text);

--
-- Name: ix_app_portal_posts_event; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_app_portal_posts_event ON public.app_portal_posts USING btree (kind, event_at);

--
-- Name: ix_app_portal_posts_kind_created; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_app_portal_posts_kind_created ON public.app_portal_posts USING btree (kind, published, created_at DESC);

--
-- Name: ix_app_releases_latest; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_app_releases_latest ON public.app_releases USING btree (app_target, is_published, version_code DESC, published_at DESC, id DESC);

--
-- Name: ix_app_users_username_live; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_app_users_username_live ON public.app_users USING btree (username) WHERE (is_deleted = false);

--
-- Name: ix_audit_logs_action; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_audit_logs_action ON public.audit_logs USING btree (action, occurred_at DESC);

--
-- Name: ix_audit_logs_entity; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_audit_logs_entity ON public.audit_logs USING btree (entity, occurred_at DESC);

--
-- Name: ix_audit_logs_occurred; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_audit_logs_occurred ON public.audit_logs USING btree (occurred_at DESC);

--
-- Name: ix_audit_logs_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_audit_logs_time ON public.audit_logs USING btree (occurred_at DESC);

--
-- Name: ix_audit_logs_username; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_audit_logs_username ON public.audit_logs USING btree (username, occurred_at DESC);

--
-- Name: ix_cash_collection_events_order; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cash_collection_events_order ON public.cash_collection_events USING btree (order_id, occurred_at, id);

--
-- Name: ix_cash_collection_orders_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cash_collection_orders_customer ON public.cash_collection_orders USING btree (customer_id, created_at DESC);

--
-- Name: ix_cash_collection_orders_driver; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cash_collection_orders_driver ON public.cash_collection_orders USING btree (driver_username, status, scheduled_date DESC);

--
-- Name: ix_cash_collection_orders_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cash_collection_orders_status ON public.cash_collection_orders USING btree (status, handover_due_at, created_at DESC);

--
-- Name: ix_cash_count_sessions_order; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cash_count_sessions_order ON public.cash_count_sessions USING btree (order_id, stage, revision DESC);

--
-- Name: ix_cash_fund_manual_occurred; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cash_fund_manual_occurred ON public.cash_fund_manual_entries USING btree (occurred_at DESC);

--
-- Name: ix_cham_cong_face_enrollment_samples_request; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cham_cong_face_enrollment_samples_request ON public.cham_cong_face_enrollment_samples USING btree (request_id);

--
-- Name: ix_cham_cong_face_enrollments_status_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cham_cong_face_enrollments_status_time ON public.cham_cong_face_enrollments USING btree (status, requested_at DESC);

--
-- Name: ix_cham_cong_face_username; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cham_cong_face_username ON public.cham_cong_face USING btree (username, created_at DESC);

--
-- Name: ix_cham_cong_log_username_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cham_cong_log_username_time ON public.cham_cong_log USING btree (username, occurred_at DESC);

--
-- Name: ix_cham_cong_offline_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cham_cong_offline_status ON public.cham_cong_offline USING btree (status, synced_at DESC);

--
-- Name: ix_customer_opening_balances_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_customer_opening_balances_date ON public.customer_opening_balances USING btree (as_of_date);

--
-- Name: ix_customers_name_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_customers_name_active ON public.customers USING btree (name) WHERE (is_active = true);

--
-- Name: ix_document_line_edits_doc; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_document_line_edits_doc ON public.document_line_edits USING btree (document_id, id);

--
-- Name: ix_document_lines_document; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_document_lines_document ON public.document_lines USING btree (document_id, line_no);

--
-- Name: ix_document_lines_product; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_document_lines_product ON public.document_lines USING btree (product_id) WHERE (product_id IS NOT NULL);

--
-- Name: ix_document_lines_source; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_document_lines_source ON public.document_lines USING btree (source_document_id, source_line_no) WHERE (source_document_id IS NOT NULL);

--
-- Name: ix_documents_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_documents_customer ON public.documents USING btree (customer_id, doc_date DESC);

--
-- Name: ix_documents_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_documents_date ON public.documents USING btree (doc_date DESC, voucher_no DESC);

--
-- Name: ix_documents_delivery_driver; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_documents_delivery_driver ON public.documents USING btree (delivery_driver_username, doc_date DESC) WHERE ((delivery_mode)::text = 'driver'::text);

--
-- Name: ix_documents_delivery_pending_return; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_documents_delivery_pending_return ON public.documents USING btree (doc_date DESC) WHERE (((delivery_mode)::text = 'driver'::text) AND (delivery_returned_at IS NULL));

--
-- Name: ix_documents_type_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_documents_type_date ON public.documents USING btree (document_type, doc_date DESC, voucher_no DESC);

--
-- Name: ix_gia_cong_hang_hoa_phieu; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_gia_cong_hang_hoa_phieu ON public.gia_cong_hang_hoa USING btree (phieu_id);

--
-- Name: ix_gia_cong_phieu_filter; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_gia_cong_phieu_filter ON public.gia_cong_phieu USING btree (id DESC, ngay_lap DESC);

--
-- Name: ix_help_faqs_order; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_help_faqs_order ON public.help_faqs USING btree (is_published, category, order_no);

--
-- Name: ix_hr_attendance_corrections_employee_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_attendance_corrections_employee_date ON public.hr_attendance_corrections USING btree (employee_id, work_date, loai, applied_at DESC, id DESC);

--
-- Name: ix_hr_attendance_corrections_user_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_attendance_corrections_user_date ON public.hr_attendance_corrections USING btree (lower((username)::text), work_date, loai, applied_at DESC, id DESC);

--
-- Name: ix_hr_attendance_reminders_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_attendance_reminders_status ON public.hr_attendance_reminders USING btree (status, work_date, employee_id);

--
-- Name: ix_hr_bank_accounts_emp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_bank_accounts_emp ON public.hr_bank_accounts USING btree (employee_id, is_default DESC, created_at);

--
-- Name: ix_hr_contracts_emp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_contracts_emp ON public.hr_contracts USING btree (employee_id, start_date DESC);

--
-- Name: ix_hr_device_tokens_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_device_tokens_user ON public.hr_device_tokens USING btree (username);

--
-- Name: ix_hr_documents_emp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_documents_emp ON public.hr_documents USING btree (employee_id, doc_type);

--
-- Name: ix_hr_employee_positions_position; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_employee_positions_position ON public.hr_employee_positions USING btree (position_id, employee_id);

--
-- Name: ix_hr_employees_department; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_employees_department ON public.hr_employees USING btree (department_id);

--
-- Name: ix_hr_employees_location; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_employees_location ON public.hr_employees USING btree (location_id);

--
-- Name: ix_hr_employees_manager; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_employees_manager ON public.hr_employees USING btree (manager_id);

--
-- Name: ix_hr_employees_position; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_employees_position ON public.hr_employees USING btree (position_id);

--
-- Name: ix_hr_holidays_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_holidays_date ON public.hr_holidays USING btree (holiday_date);

--
-- Name: ix_hr_payout_voucher_events_voucher; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_payout_voucher_events_voucher ON public.hr_payout_voucher_events USING btree (voucher_id, occurred_at, id);

--
-- Name: ix_hr_payout_vouchers_emp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_payout_vouchers_emp ON public.hr_payout_vouchers USING btree (employee_id, created_at DESC);

--
-- Name: ix_hr_payout_vouchers_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_payout_vouchers_status ON public.hr_payout_vouchers USING btree (status, created_at DESC);

--
-- Name: ix_hr_payout_vouchers_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_payout_vouchers_time ON public.hr_payout_vouchers USING btree (created_at DESC);

--
-- Name: ix_hr_payslip_history_employee_period; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_payslip_history_employee_period ON public.hr_payslip_history USING btree (employee_id, period, occurred_at DESC);

--
-- Name: ix_hr_payslip_history_payslip; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_payslip_history_payslip ON public.hr_payslip_history USING btree (payslip_id, revision DESC);

--
-- Name: ix_hr_penalties_emp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_penalties_emp ON public.hr_penalties USING btree (employee_id, penalty_date DESC);

--
-- Name: ix_hr_penalties_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_penalties_status ON public.hr_penalties USING btree (status, penalty_date DESC);

--
-- Name: ix_hr_penalty_refunds_emp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_penalty_refunds_emp ON public.hr_penalty_refunds USING btree (employee_id, created_at DESC);

--
-- Name: ix_hr_penalty_refunds_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_penalty_refunds_status ON public.hr_penalty_refunds USING btree (status, created_at DESC);

--
-- Name: ix_hr_request_approvals_approver; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_request_approvals_approver ON public.hr_request_approvals USING btree (approver_username, status);

--
-- Name: ix_hr_request_attachments_request; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_request_attachments_request ON public.hr_request_attachments USING btree (request_id, id);

--
-- Name: ix_hr_requests_requester; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_requests_requester ON public.hr_requests USING btree (requester_username, created_at DESC);

--
-- Name: ix_hr_requests_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_requests_status ON public.hr_requests USING btree (status, created_at DESC);

--
-- Name: ix_hr_salary_raises_contract; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_salary_raises_contract ON public.hr_salary_raises USING btree (contract_id, effective_period);

--
-- Name: ix_hr_salary_raises_emp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_salary_raises_emp ON public.hr_salary_raises USING btree (employee_id, effective_period);

--
-- Name: ix_hr_shift_assignments_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hr_shift_assignments_date ON public.hr_shift_assignments USING btree (work_date);

--
-- Name: ix_onboarding_employee; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_onboarding_employee ON public.hr_onboarding_tasks USING btree (employee_id, due_at);

--
-- Name: ix_payments_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_payments_customer ON public.payments USING btree (customer_id, pay_date DESC);

--
-- Name: ix_purchase_lines_product; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_lines_product ON public.purchase_lines USING btree (product_id) WHERE (product_id IS NOT NULL);

--
-- Name: ix_purchase_lines_purchase; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_lines_purchase ON public.purchase_lines USING btree (purchase_id, line_no);

--
-- Name: ix_purchases_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchases_date ON public.purchases USING btree (doc_date DESC, voucher_no DESC);

--
-- Name: ix_purchases_supplier; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchases_supplier ON public.purchases USING btree (supplier_id, doc_date DESC);

--
-- Name: ix_recovery_username; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_recovery_username ON public.password_recovery_codes USING btree (username);

--
-- Name: ix_survey_answers_response; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_survey_answers_response ON public.survey_answers USING btree (response_id);

--
-- Name: ix_survey_answers_survey; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_survey_answers_survey ON public.survey_answers USING btree (question_id);

--
-- Name: ix_survey_questions_survey; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_survey_questions_survey ON public.survey_questions USING btree (survey_id, order_no);

--
-- Name: ix_user_role_history_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_user_role_history_user ON public.user_role_history USING btree (username, occurred_at DESC);

--
-- Name: ix_user_roles_username; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_user_roles_username ON public.user_roles USING btree (username);

--
-- Name: ix_user_sessions_presence; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_user_sessions_presence ON public.user_sessions USING btree (username, is_active, last_seen DESC);

--
-- Name: ix_web_notifications_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_web_notifications_user ON public.web_notifications USING btree (lower((username)::text), created_at DESC);

--
-- Name: ix_work_task_events_task; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_work_task_events_task ON public.work_task_events USING btree (task_id, id);

--
-- Name: ix_work_tasks_assignee; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_work_tasks_assignee ON public.work_tasks USING btree (assignee_username, status);

--
-- Name: ix_work_tasks_assigner; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_work_tasks_assigner ON public.work_tasks USING btree (assigner_username, status);

--
-- Name: ux_app_outbox_dedupe; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_app_outbox_dedupe ON public.app_outbox USING btree (dedupe_key) WHERE ((dedupe_key)::text <> ''::text);

--
-- Name: ux_app_users_username_ci_active; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_app_users_username_ci_active ON public.app_users USING btree (lower((username)::text)) WHERE (is_deleted = false);

--
-- Name: ux_cash_collection_active_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_cash_collection_active_customer ON public.cash_collection_orders USING btree (customer_id) WHERE ((status)::text = ANY ((ARRAY['Assigned'::character varying, 'Accepted'::character varying, 'PendingHandover'::character varying, 'Variance'::character varying])::text[]));

--
-- Name: ux_cash_collection_events_lifecycle; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_cash_collection_events_lifecycle ON public.cash_collection_events USING btree (order_id, action) WHERE ((action)::text = ANY ((ARRAY['created'::character varying, 'accepted'::character varying, 'collected'::character varying, 'failed'::character varying, 'completed'::character varying, 'cancelled'::character varying])::text[]));

--
-- Name: ux_cash_fund_opening; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_cash_fund_opening ON public.cash_fund_manual_entries USING btree (is_opening) WHERE ((is_opening = true) AND (reversed_at IS NULL));

--
-- Name: ux_cham_cong_face_enrollments_pending_user; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_cham_cong_face_enrollments_pending_user ON public.cham_cong_face_enrollments USING btree (lower((username)::text)) WHERE ((status)::text = 'pending'::text);

--
-- Name: ux_hr_contracts_no; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_hr_contracts_no ON public.hr_contracts USING btree (contract_no) WHERE ((contract_no)::text <> ''::text);

--
-- Name: ux_hr_employee_positions_primary; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_hr_employee_positions_primary ON public.hr_employee_positions USING btree (employee_id) WHERE (is_primary = true);

--
-- Name: ux_hr_employees_code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_hr_employees_code ON public.hr_employees USING btree (employee_code) WHERE ((employee_code)::text <> ''::text);

--
-- Name: ux_hr_employees_username; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_hr_employees_username ON public.hr_employees USING btree (username) WHERE ((username)::text <> ''::text);

--
-- Name: ux_hr_employees_username_ci; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_hr_employees_username_ci ON public.hr_employees USING btree (lower((username)::text)) WHERE (btrim((username)::text) <> ''::text);

--
-- Name: ux_hr_holidays_date_type; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_hr_holidays_date_type ON public.hr_holidays USING btree (holiday_date, holiday_type);

--
-- Name: ux_hr_leave_balances; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_hr_leave_balances ON public.hr_leave_balances USING btree (employee_id, year, leave_type);

--
-- Name: ux_hr_payout_voucher_events_lifecycle; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_hr_payout_voucher_events_lifecycle ON public.hr_payout_voucher_events USING btree (voucher_id, action) WHERE ((action)::text = ANY ((ARRAY['created'::character varying, 'recipient_confirmed'::character varying, 'approved'::character varying, 'rejected'::character varying, 'cancelled'::character varying, 'completed'::character varying])::text[]));

--
-- Name: ux_hr_payout_vouchers_qr; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_hr_payout_vouchers_qr ON public.hr_payout_vouchers USING btree (qr_code) WHERE ((qr_code)::text <> ''::text);

--
-- Name: ux_hr_payout_vouchers_source_v2; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_hr_payout_vouchers_source_v2 ON public.hr_payout_vouchers USING btree (source_kind, source_id) WHERE ((source_id IS NOT NULL) AND ((status)::text <> ALL ((ARRAY['Cancelled'::character varying, 'Rejected'::character varying])::text[])));

--
-- Name: ux_hr_payslips_emp_period; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_hr_payslips_emp_period ON public.hr_payslips USING btree (employee_id, period);

--
-- Name: ux_hr_request_approvals; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_hr_request_approvals ON public.hr_request_approvals USING btree (request_id, step_no);

--
-- Name: ux_hr_requests_pending_forgot; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_hr_requests_pending_forgot ON public.hr_requests USING btree (employee_id, ((payload ->> 'date'::text)), ((payload ->> 'direction'::text))) WHERE (((req_type)::text = 'forgot_checkin'::text) AND ((status)::text = 'Pending'::text));

--
-- Name: ux_hr_shift_assignments; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_hr_shift_assignments ON public.hr_shift_assignments USING btree (employee_id, work_date);

--
-- Name: ux_payments_source; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_payments_source ON public.payments USING btree (source_kind, source_id) WHERE ((source_id IS NOT NULL) AND ((source_kind)::text <> ''::text));

--
-- Name: ux_products_code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_products_code ON public.products USING btree (lower((code)::text)) WHERE ((code)::text <> ''::text);

--
-- Name: ux_products_name_spec; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_products_name_spec ON public.products USING btree (lower((name)::text), lower((spec)::text));

--
-- Name: ux_suppliers_name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_suppliers_name ON public.suppliers USING btree (lower((name)::text));

--
-- Name: ux_survey_once; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_survey_once ON public.survey_responses USING btree (survey_id, respondent_hash) WHERE (respondent_hash IS NOT NULL);

--
-- Name: ux_web_notifications_event; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_web_notifications_event ON public.web_notifications USING btree (lower((username)::text), notif_id) WHERE ((notif_id)::text <> ''::text);

--
-- Name: ux_work_tasks_delivery_document; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_work_tasks_delivery_document ON public.work_tasks USING btree (source_document_id) WHERE (((source_kind)::text = 'delivery'::text) AND (source_document_id IS NOT NULL));

--
-- Name: app_config ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.app_config FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('config');

--
-- Name: app_feedbacks ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.app_feedbacks FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('feedback');

--
-- Name: app_general_feedback ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.app_general_feedback FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('feedback');

--
-- Name: app_portal_about ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.app_portal_about FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('portal');

--
-- Name: app_portal_posts ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.app_portal_posts FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('portal');

--
-- Name: app_releases ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.app_releases FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('release');

--
-- Name: app_settings ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.app_settings FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('presence');

--
-- Name: app_support_tickets ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.app_support_tickets FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('feedback');

--
-- Name: app_survey_responses ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.app_survey_responses FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: app_users ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.app_users FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('presence');

--
-- Name: audit_logs ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.audit_logs FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('audit');

--
-- Name: cash_collection_events ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.cash_collection_events FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data', 'hr');

--
-- Name: cash_collection_orders ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.cash_collection_orders FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data', 'hr');

--
-- Name: cash_count_lines ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.cash_count_lines FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data', 'hr');

--
-- Name: cash_count_sessions ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.cash_count_sessions FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data', 'hr');

--
-- Name: cham_cong_face ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.cham_cong_face FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('attendance', 'hr');

--
-- Name: cham_cong_face_enrollment_samples ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.cham_cong_face_enrollment_samples FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('attendance', 'hr');

--
-- Name: cham_cong_face_enrollments ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.cham_cong_face_enrollments FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('attendance', 'hr');

--
-- Name: cham_cong_log ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.cham_cong_log FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('attendance', 'hr');

--
-- Name: cham_cong_offline ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.cham_cong_offline FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('attendance', 'hr');

--
-- Name: cham_cong_qr_sites ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.cham_cong_qr_sites FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('attendance', 'hr');

--
-- Name: customer_aliases ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.customer_aliases FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: customer_opening_balances ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.customer_opening_balances FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: customers ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.customers FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: document_lines ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.document_lines FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: documents ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.documents FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: gia_cong_hang_hoa ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.gia_cong_hang_hoa FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: gia_cong_phieu ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.gia_cong_phieu FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: help_faqs ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.help_faqs FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: hr_anniversary_letter ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_anniversary_letter FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_approval_delegations ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_approval_delegations FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_attendance_corrections ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_attendance_corrections FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('attendance', 'hr');

--
-- Name: hr_attendance_reminders ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_attendance_reminders FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('attendance', 'hr');

--
-- Name: hr_bank_accounts ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_bank_accounts FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_contracts ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_contracts FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_departments ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_departments FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_documents ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_documents FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_employee_benefits ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_employee_benefits FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('talent');

--
-- Name: hr_employee_positions ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_employee_positions FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_employee_rewards ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_employee_rewards FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('talent');

--
-- Name: hr_employees ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_employees FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_holidays ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_holidays FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_job_positions ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_job_positions FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_leave_balances ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_leave_balances FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_locations ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_locations FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_onboarding_tasks ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_onboarding_tasks FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('talent');

--
-- Name: hr_payout_categories ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_payout_categories FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_payout_voucher_events ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_payout_voucher_events FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_payout_vouchers ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_payout_vouchers FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_payslip_history ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_payslip_history FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_payslip_inquiries ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_payslip_inquiries FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_payslips ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_payslips FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_penalties ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_penalties FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_penalty_ledger ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_penalty_ledger FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_penalty_refunds ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_penalty_refunds FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_performance_goals ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_performance_goals FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('talent');

--
-- Name: hr_performance_reviews ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_performance_reviews FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('talent');

--
-- Name: hr_request_approvals ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_request_approvals FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_request_attachments ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_request_attachments FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_requests ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_requests FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_salaries ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_salaries FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_salary_raises ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_salary_raises FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_shift_assignments ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_shift_assignments FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_shifts ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_shifts FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: hr_training_courses ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_training_courses FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('talent');

--
-- Name: hr_training_enrollments ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.hr_training_enrollments FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('talent');

--
-- Name: password_reset_requests ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.password_reset_requests FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('presence');

--
-- Name: payments ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.payments FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: products ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.products FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: purchase_lines ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.purchase_lines FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: purchases ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.purchases FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: registration_codes ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.registration_codes FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('presence');

--
-- Name: suppliers ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.suppliers FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: survey_answers ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.survey_answers FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: survey_questions ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.survey_questions FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: survey_responses ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.survey_responses FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: surveys ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.surveys FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('data');

--
-- Name: system_roles ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.system_roles FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('presence');

--
-- Name: user_roles ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.user_roles FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('presence');

--
-- Name: user_sessions ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.user_sessions FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('presence');

--
-- Name: web_diamond_members ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.web_diamond_members FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('presence');

--
-- Name: web_notifications ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.web_notifications FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('notify');

--
-- Name: web_system_settings ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.web_system_settings FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('hr');

--
-- Name: web_user_avatars ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.web_user_avatars FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('presence');

--
-- Name: web_verified_users ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.web_verified_users FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('presence');

--
-- Name: work_access_requests ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.work_access_requests FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('presence');

--
-- Name: work_task_events ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.work_task_events FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('tasks');

--
-- Name: work_tasks ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.work_tasks FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('tasks');

--
-- Name: cash_collection_events trg_cash_collection_events_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_cash_collection_events_immutable BEFORE DELETE OR UPDATE ON public.cash_collection_events FOR EACH ROW EXECUTE FUNCTION public.prevent_cash_collection_event_mutation();

--
-- Name: documents trg_documents_issued_voucher_no_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_documents_issued_voucher_no_immutable BEFORE UPDATE OF voucher_no ON public.documents FOR EACH ROW EXECUTE FUNCTION public.prevent_issued_warehouse_voucher_no_change();

--
-- Name: documents trg_documents_no_physical_delete; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_documents_no_physical_delete BEFORE DELETE ON public.documents FOR EACH ROW EXECUTE FUNCTION public.prevent_document_physical_delete();

--
-- Name: hr_payout_voucher_events trg_hr_payout_voucher_events_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_hr_payout_voucher_events_immutable BEFORE DELETE OR UPDATE ON public.hr_payout_voucher_events FOR EACH ROW EXECUTE FUNCTION public.prevent_hr_payout_voucher_event_mutation();

--
-- Name: hr_payslip_history trg_hr_payslip_history_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_hr_payslip_history_immutable BEFORE DELETE OR UPDATE ON public.hr_payslip_history FOR EACH ROW EXECUTE FUNCTION public.prevent_hr_payslip_history_mutation();

--
-- Name: app_survey_responses app_survey_responses_survey_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_survey_responses
    ADD CONSTRAINT app_survey_responses_survey_id_fkey FOREIGN KEY (survey_id) REFERENCES public.app_surveys(id) ON DELETE CASCADE;

--
-- Name: cash_collection_orders cash_collection_orders_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_collection_orders
    ADD CONSTRAINT cash_collection_orders_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE RESTRICT;

--
-- Name: cash_collection_orders cash_collection_orders_driver_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_collection_orders
    ADD CONSTRAINT cash_collection_orders_driver_employee_id_fkey FOREIGN KEY (driver_employee_id) REFERENCES public.hr_employees(id) ON DELETE RESTRICT;

--
-- Name: cash_collection_orders cash_collection_orders_payment_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_collection_orders
    ADD CONSTRAINT cash_collection_orders_payment_id_fkey FOREIGN KEY (payment_id) REFERENCES public.payments(id) ON DELETE RESTRICT;

--
-- Name: cash_count_lines cash_count_lines_session_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_count_lines
    ADD CONSTRAINT cash_count_lines_session_id_fkey FOREIGN KEY (session_id) REFERENCES public.cash_count_sessions(id) ON DELETE RESTRICT;

--
-- Name: cash_count_sessions cash_count_sessions_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_count_sessions
    ADD CONSTRAINT cash_count_sessions_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.cash_collection_orders(id) ON DELETE RESTRICT;

--
-- Name: cham_cong_face_enrollment_samples cham_cong_face_enrollment_samples_request_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cham_cong_face_enrollment_samples
    ADD CONSTRAINT cham_cong_face_enrollment_samples_request_id_fkey FOREIGN KEY (request_id) REFERENCES public.cham_cong_face_enrollments(id) ON DELETE CASCADE;

--
-- Name: customer_aliases customer_aliases_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_aliases
    ADD CONSTRAINT customer_aliases_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;

--
-- Name: customer_opening_balances customer_opening_balances_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_opening_balances
    ADD CONSTRAINT customer_opening_balances_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;

--
-- Name: document_issued_lines document_issued_lines_document_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.document_issued_lines
    ADD CONSTRAINT document_issued_lines_document_id_fkey FOREIGN KEY (document_id) REFERENCES public.documents(id) ON DELETE CASCADE;

--
-- Name: document_line_edits document_line_edits_document_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.document_line_edits
    ADD CONSTRAINT document_line_edits_document_id_fkey FOREIGN KEY (document_id) REFERENCES public.documents(id) ON DELETE CASCADE;

--
-- Name: document_lines document_lines_document_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.document_lines
    ADD CONSTRAINT document_lines_document_id_fkey FOREIGN KEY (document_id) REFERENCES public.documents(id) ON DELETE CASCADE;

--
-- Name: document_lines document_lines_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.document_lines
    ADD CONSTRAINT document_lines_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE SET NULL;

--
-- Name: document_lines document_lines_source_document_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.document_lines
    ADD CONSTRAINT document_lines_source_document_id_fkey FOREIGN KEY (source_document_id) REFERENCES public.documents(id) ON DELETE RESTRICT;

--
-- Name: documents documents_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.documents
    ADD CONSTRAINT documents_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE SET NULL;

--
-- Name: app_users fk_app_users_system_role; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_users
    ADD CONSTRAINT fk_app_users_system_role FOREIGN KEY (role) REFERENCES public.system_roles(code);

--
-- Name: user_roles fk_user_roles_system_role; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_roles
    ADD CONSTRAINT fk_user_roles_system_role FOREIGN KEY (role) REFERENCES public.system_roles(code);

--
-- Name: gia_cong_hang_hoa gia_cong_hang_hoa_phieu_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gia_cong_hang_hoa
    ADD CONSTRAINT gia_cong_hang_hoa_phieu_id_fkey FOREIGN KEY (phieu_id) REFERENCES public.gia_cong_phieu(id) ON DELETE CASCADE;

--
-- Name: hr_attendance_corrections hr_attendance_corrections_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_attendance_corrections
    ADD CONSTRAINT hr_attendance_corrections_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_attendance_corrections hr_attendance_corrections_request_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_attendance_corrections
    ADD CONSTRAINT hr_attendance_corrections_request_id_fkey FOREIGN KEY (request_id) REFERENCES public.hr_requests(id) ON DELETE RESTRICT;

--
-- Name: hr_attendance_reminders hr_attendance_reminders_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_attendance_reminders
    ADD CONSTRAINT hr_attendance_reminders_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_attendance_reminders hr_attendance_reminders_request_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_attendance_reminders
    ADD CONSTRAINT hr_attendance_reminders_request_id_fkey FOREIGN KEY (request_id) REFERENCES public.hr_requests(id) ON DELETE SET NULL;

--
-- Name: hr_bank_accounts hr_bank_accounts_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_bank_accounts
    ADD CONSTRAINT hr_bank_accounts_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_contracts hr_contracts_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_contracts
    ADD CONSTRAINT hr_contracts_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_departments hr_departments_parent_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_departments
    ADD CONSTRAINT hr_departments_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES public.hr_departments(id) ON DELETE SET NULL;

--
-- Name: hr_documents hr_documents_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_documents
    ADD CONSTRAINT hr_documents_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_employee_benefits hr_employee_benefits_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_employee_benefits
    ADD CONSTRAINT hr_employee_benefits_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_employee_positions hr_employee_positions_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_employee_positions
    ADD CONSTRAINT hr_employee_positions_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_employee_positions hr_employee_positions_position_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_employee_positions
    ADD CONSTRAINT hr_employee_positions_position_id_fkey FOREIGN KEY (position_id) REFERENCES public.hr_job_positions(id) ON DELETE RESTRICT;

--
-- Name: hr_employee_rewards hr_employee_rewards_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_employee_rewards
    ADD CONSTRAINT hr_employee_rewards_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_employees hr_employees_department_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_employees
    ADD CONSTRAINT hr_employees_department_id_fkey FOREIGN KEY (department_id) REFERENCES public.hr_departments(id) ON DELETE SET NULL;

--
-- Name: hr_employees hr_employees_location_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_employees
    ADD CONSTRAINT hr_employees_location_id_fkey FOREIGN KEY (location_id) REFERENCES public.hr_locations(id) ON DELETE SET NULL;

--
-- Name: hr_employees hr_employees_manager_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_employees
    ADD CONSTRAINT hr_employees_manager_id_fkey FOREIGN KEY (manager_id) REFERENCES public.hr_employees(id) ON DELETE SET NULL;

--
-- Name: hr_employees hr_employees_position_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_employees
    ADD CONSTRAINT hr_employees_position_id_fkey FOREIGN KEY (position_id) REFERENCES public.hr_job_positions(id) ON DELETE RESTRICT;

--
-- Name: hr_employees hr_employees_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_employees
    ADD CONSTRAINT hr_employees_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.app_users(id) ON DELETE SET NULL;

--
-- Name: hr_job_positions hr_job_positions_default_role_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_job_positions
    ADD CONSTRAINT hr_job_positions_default_role_fkey FOREIGN KEY (default_role) REFERENCES public.system_roles(code);

--
-- Name: hr_leave_balances hr_leave_balances_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_leave_balances
    ADD CONSTRAINT hr_leave_balances_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_onboarding_tasks hr_onboarding_tasks_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_onboarding_tasks
    ADD CONSTRAINT hr_onboarding_tasks_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_payout_vouchers hr_payout_vouchers_category_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_payout_vouchers
    ADD CONSTRAINT hr_payout_vouchers_category_id_fkey FOREIGN KEY (category_id) REFERENCES public.hr_payout_categories(id);

--
-- Name: hr_payout_vouchers hr_payout_vouchers_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_payout_vouchers
    ADD CONSTRAINT hr_payout_vouchers_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_payslip_inquiries hr_payslip_inquiries_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_payslip_inquiries
    ADD CONSTRAINT hr_payslip_inquiries_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_payslip_inquiries hr_payslip_inquiries_payslip_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_payslip_inquiries
    ADD CONSTRAINT hr_payslip_inquiries_payslip_id_fkey FOREIGN KEY (payslip_id) REFERENCES public.hr_payslips(id) ON DELETE CASCADE;

--
-- Name: hr_payslips hr_payslips_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_payslips
    ADD CONSTRAINT hr_payslips_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_penalties hr_penalties_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_penalties
    ADD CONSTRAINT hr_penalties_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_penalty_ledger hr_penalty_ledger_penalty_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_penalty_ledger
    ADD CONSTRAINT hr_penalty_ledger_penalty_id_fkey FOREIGN KEY (penalty_id) REFERENCES public.hr_penalties(id) ON DELETE CASCADE;

--
-- Name: hr_penalty_refunds hr_penalty_refunds_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_penalty_refunds
    ADD CONSTRAINT hr_penalty_refunds_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_performance_goals hr_performance_goals_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_performance_goals
    ADD CONSTRAINT hr_performance_goals_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_performance_reviews hr_performance_reviews_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_performance_reviews
    ADD CONSTRAINT hr_performance_reviews_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_request_approvals hr_request_approvals_request_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_request_approvals
    ADD CONSTRAINT hr_request_approvals_request_id_fkey FOREIGN KEY (request_id) REFERENCES public.hr_requests(id) ON DELETE CASCADE;

--
-- Name: hr_request_attachments hr_request_attachments_request_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_request_attachments
    ADD CONSTRAINT hr_request_attachments_request_id_fkey FOREIGN KEY (request_id) REFERENCES public.hr_requests(id) ON DELETE CASCADE;

--
-- Name: hr_requests hr_requests_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_requests
    ADD CONSTRAINT hr_requests_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_salaries hr_salaries_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_salaries
    ADD CONSTRAINT hr_salaries_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_salary_raises hr_salary_raises_contract_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_salary_raises
    ADD CONSTRAINT hr_salary_raises_contract_id_fkey FOREIGN KEY (contract_id) REFERENCES public.hr_contracts(id) ON DELETE CASCADE;

--
-- Name: hr_salary_raises hr_salary_raises_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_salary_raises
    ADD CONSTRAINT hr_salary_raises_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_shift_assignments hr_shift_assignments_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_shift_assignments
    ADD CONSTRAINT hr_shift_assignments_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: hr_shift_assignments hr_shift_assignments_shift_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_shift_assignments
    ADD CONSTRAINT hr_shift_assignments_shift_id_fkey FOREIGN KEY (shift_id) REFERENCES public.hr_shifts(id) ON DELETE CASCADE;

--
-- Name: hr_training_enrollments hr_training_enrollments_course_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_training_enrollments
    ADD CONSTRAINT hr_training_enrollments_course_id_fkey FOREIGN KEY (course_id) REFERENCES public.hr_training_courses(id) ON DELETE CASCADE;

--
-- Name: hr_training_enrollments hr_training_enrollments_employee_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hr_training_enrollments
    ADD CONSTRAINT hr_training_enrollments_employee_id_fkey FOREIGN KEY (employee_id) REFERENCES public.hr_employees(id) ON DELETE CASCADE;

--
-- Name: payments payments_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE SET NULL;

--
-- Name: purchase_lines purchase_lines_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_lines
    ADD CONSTRAINT purchase_lines_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE SET NULL;

--
-- Name: purchase_lines purchase_lines_purchase_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_lines
    ADD CONSTRAINT purchase_lines_purchase_id_fkey FOREIGN KEY (purchase_id) REFERENCES public.purchases(id) ON DELETE CASCADE;

--
-- Name: purchases purchases_supplier_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchases
    ADD CONSTRAINT purchases_supplier_id_fkey FOREIGN KEY (supplier_id) REFERENCES public.suppliers(id) ON DELETE SET NULL;

--
-- Name: survey_answers survey_answers_response_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.survey_answers
    ADD CONSTRAINT survey_answers_response_id_fkey FOREIGN KEY (response_id) REFERENCES public.survey_responses(id) ON DELETE CASCADE;

--
-- Name: survey_questions survey_questions_survey_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.survey_questions
    ADD CONSTRAINT survey_questions_survey_id_fkey FOREIGN KEY (survey_id) REFERENCES public.surveys(id) ON DELETE CASCADE;

--
-- Name: survey_responses survey_responses_survey_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.survey_responses
    ADD CONSTRAINT survey_responses_survey_id_fkey FOREIGN KEY (survey_id) REFERENCES public.surveys(id) ON DELETE CASCADE;

--
-- Name: work_task_events work_task_events_task_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.work_task_events
    ADD CONSTRAINT work_task_events_task_id_fkey FOREIGN KEY (task_id) REFERENCES public.work_tasks(id) ON DELETE CASCADE;

--
-- PostgreSQL database dump complete
--

\unrestrict 3iCACu3dwQW7dtrXlp2RW127fmzPXR02KdoXvt4obzWec3e2OfHsn824yNlUDyd
