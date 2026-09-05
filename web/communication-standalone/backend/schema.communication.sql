-- Communication schema extracted from docs/backend-schema.sql.

-- Apply only to the standalone communication database/host.

ALTER TABLE public.app_config
    ADD COLUMN IF NOT EXISTS call_config jsonb DEFAULT '{}'::jsonb NOT NULL;

--
-- Name: web_call_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.web_call_events (
    id bigint NOT NULL,
    to_username character varying(128) NOT NULL,
    from_username character varying(128) DEFAULT ''::character varying NOT NULL,
    from_name character varying(200) DEFAULT ''::character varying NOT NULL,
    call_id character varying(64) DEFAULT ''::character varying NOT NULL,
    media character varying(16) DEFAULT 'audio'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    seen boolean DEFAULT false NOT NULL
);

--
-- Name: web_call_events_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.web_call_events_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: web_call_events_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.web_call_events_id_seq OWNED BY public.web_call_events.id;

--
-- Name: web_call_history; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.web_call_history (
    id bigint NOT NULL,
    username character varying(128) NOT NULL,
    peer_username character varying(128) NOT NULL,
    peer_name character varying(200) DEFAULT ''::character varying NOT NULL,
    call_id character varying(64) NOT NULL,
    media character varying(16) DEFAULT 'audio'::character varying NOT NULL,
    direction character varying(16) DEFAULT 'outgoing'::character varying NOT NULL,
    outcome character varying(32) DEFAULT 'ended'::character varying NOT NULL,
    started_at timestamp with time zone,
    ended_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    duration_seconds integer DEFAULT 0 NOT NULL
);

--
-- Name: web_call_history_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.web_call_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: web_call_history_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.web_call_history_id_seq OWNED BY public.web_call_history.id;

--
-- Name: web_chat_conversations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.web_chat_conversations (
    id uuid NOT NULL,
    is_group boolean DEFAULT false NOT NULL,
    title character varying(200) DEFAULT ''::character varying NOT NULL,
    created_by character varying(128) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: web_chat_members; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.web_chat_members (
    conversation_id uuid NOT NULL,
    username character varying(128) NOT NULL,
    last_read_at timestamp with time zone,
    joined_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    is_pinned boolean DEFAULT false NOT NULL,
    is_hidden boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone
);

--
-- Name: web_chat_messages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.web_chat_messages (
    id bigint NOT NULL,
    conversation_id uuid NOT NULL,
    sender_username character varying(128) NOT NULL,
    body text NOT NULL,
    edited_at timestamp with time zone,
    is_removed boolean DEFAULT false NOT NULL,
    is_forwarded boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    kind character varying(16) DEFAULT 'text'::character varying NOT NULL,
    file_name character varying(260),
    file_size bigint,
    file_mime character varying(160),
    client_message_id character varying(128),
    has_blob boolean DEFAULT false NOT NULL,
    blob_expires_at timestamp with time zone
);

--
-- Name: web_chat_messages_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.web_chat_messages_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: web_chat_messages_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.web_chat_messages_id_seq OWNED BY public.web_chat_messages.id;

--
-- Name: web_chat_reactions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.web_chat_reactions (
    message_id bigint NOT NULL,
    username character varying(128) NOT NULL,
    emoji character varying(16) NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: web_chat_reports; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.web_chat_reports (
    id bigint NOT NULL,
    conversation_id uuid NOT NULL,
    reporter_username character varying(128) NOT NULL,
    reason character varying(500) DEFAULT ''::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

--
-- Name: web_chat_reports_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.web_chat_reports_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

--
-- Name: web_chat_reports_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.web_chat_reports_id_seq OWNED BY public.web_chat_reports.id;

--
-- Name: web_call_events id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_call_events ALTER COLUMN id SET DEFAULT nextval('public.web_call_events_id_seq'::regclass);

--
-- Name: web_call_history id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_call_history ALTER COLUMN id SET DEFAULT nextval('public.web_call_history_id_seq'::regclass);

--
-- Name: web_chat_messages id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_chat_messages ALTER COLUMN id SET DEFAULT nextval('public.web_chat_messages_id_seq'::regclass);

--
-- Name: web_chat_reports id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_chat_reports ALTER COLUMN id SET DEFAULT nextval('public.web_chat_reports_id_seq'::regclass);

--
-- Name: web_chat_members pk_web_chat_members; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_chat_members
    ADD CONSTRAINT pk_web_chat_members PRIMARY KEY (conversation_id, username);

--
-- Name: web_chat_reactions pk_web_chat_reactions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_chat_reactions
    ADD CONSTRAINT pk_web_chat_reactions PRIMARY KEY (message_id, username);

--
-- Name: web_call_events web_call_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_call_events
    ADD CONSTRAINT web_call_events_pkey PRIMARY KEY (id);

--
-- Name: web_call_history web_call_history_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_call_history
    ADD CONSTRAINT web_call_history_pkey PRIMARY KEY (id);

--
-- Name: web_chat_conversations web_chat_conversations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_chat_conversations
    ADD CONSTRAINT web_chat_conversations_pkey PRIMARY KEY (id);

--
-- Name: web_chat_messages web_chat_messages_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_chat_messages
    ADD CONSTRAINT web_chat_messages_pkey PRIMARY KEY (id);

--
-- Name: web_chat_reports web_chat_reports_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_chat_reports
    ADD CONSTRAINT web_chat_reports_pkey PRIMARY KEY (id);

CREATE INDEX ix_web_call_events_unseen ON public.web_call_events USING btree (to_username, seen, created_at DESC);

CREATE INDEX ix_web_call_history_user_time ON public.web_call_history USING btree (username, ended_at DESC);

CREATE INDEX ix_web_chat_members_list ON public.web_chat_members USING btree (username, is_hidden, deleted_at, is_pinned);

CREATE INDEX ix_web_chat_members_user ON public.web_chat_members USING btree (username, conversation_id, last_read_at);

CREATE INDEX ix_web_chat_messages_conv ON public.web_chat_messages USING btree (conversation_id, created_at DESC, id DESC);

CREATE INDEX ix_web_chat_reactions_msg ON public.web_chat_reactions USING btree (message_id);

CREATE INDEX ix_web_chat_reports_conv ON public.web_chat_reports USING btree (conversation_id, created_at DESC);

CREATE UNIQUE INDEX ux_web_call_events_target ON public.web_call_events USING btree (to_username, call_id);

CREATE UNIQUE INDEX ux_web_call_history_user_call ON public.web_call_history USING btree (username, call_id);

CREATE UNIQUE INDEX ux_web_chat_voice_client_message ON public.web_chat_messages USING btree (conversation_id, sender_username, client_message_id) WHERE ((client_message_id IS NOT NULL) AND (is_removed = false));

--
-- Name: web_chat_reports ketoanmini_publish_change; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ketoanmini_publish_change AFTER INSERT OR DELETE OR UPDATE ON public.web_chat_reports FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change('feedback');
