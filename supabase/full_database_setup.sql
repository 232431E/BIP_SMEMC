-- ============================================================
-- OptiFlow Full Database Setup
-- Use this single script for new database bootstrap or full reset/reseed.
-- ============================================================

-- OPTIONAL HARD RESET (uncomment if you want to wipe everything first)
-- drop schema public cascade;
-- create schema public;

-- ===== BEGIN: learning_schema.sql =====
-- ============================================================
-- OptiFlow Learning Module Schema + Seed (10 Qs per module)
-- - 5 topics
-- - 3 levels each (difficulty 0/1/2)
-- - 10 question pool per module, quiz draws 5 randomly (RPC)
-- ============================================================

-- =========================
-- TABLES
-- =========================
create table if not exists public.learning_topics (
  id bigserial primary key,
  category text not null,
  title text not null,
  summary text not null,
  estimated_minutes int not null default 0
);

create table if not exists public.learning_modules (
  id bigserial primary key,
  topic_id bigint not null references public.learning_topics(id) on delete cascade,
  difficulty int not null,
  title text not null
);

create table if not exists public.learning_sections (
  id bigserial primary key,
  module_id bigint not null references public.learning_modules(id) on delete cascade,
  "order" int not null default 0,
  heading text not null,
  body text not null,
  video_url text,
  image_url text
);

alter table public.learning_sections
  add column if not exists video_url text,
  add column if not exists image_url text;

create table if not exists public.learning_quiz_questions (
  id bigserial primary key,
  learning_module_id bigint not null references public.learning_modules(id) on delete cascade,
  question text not null
);

create table if not exists public.learning_quiz_options (
  id bigserial primary key,
  quiz_question_id bigint not null references public.learning_quiz_questions(id) on delete cascade,
  text text not null,
  is_correct boolean not null default false
);

create table if not exists public.learning_progress (
  id bigserial primary key,
  user_id text not null,
  topic_id bigint not null references public.learning_topics(id) on delete cascade,
  difficulty int not null,
  best_score int not null default 0,
  passed boolean not null default false,
  points_awarded int not null default 0,
  updated_at_utc timestamptz not null default now(),
  unique (user_id, topic_id, difficulty)
);

-- =========================
-- RPC: get random questions + options for a module
-- (Default: 5 questions)
-- =========================
drop function if exists public.get_random_quiz_questions(bigint, int);
create or replace function public.get_random_quiz_questions(
  p_learning_module_id bigint,
  p_question_count int default 5
)
returns table (
  question_id bigint,
  question_text text,
  option_id bigint,
  option_text text,
  is_correct boolean
)
language sql
volatile
as $$
  with picked as (
    select q.id, q.question
    from public.learning_quiz_questions q
    where q.learning_module_id = p_learning_module_id
    order by random()
    limit p_question_count
  )
  select
    p.id as question_id,
    p.question as question_text,
    o.id as option_id,
    o.text as option_text,
    o.is_correct
  from picked p
  join public.learning_quiz_options o
    on o.quiz_question_id = p.id
  order by p.id, o.id;
$$;

grant execute on function public.get_random_quiz_questions(bigint, int) to authenticated;

-- =========================================================
-- COMMUNITY HUB TABLES + SEED DATA
-- =========================================================

create table if not exists public.reward_profiles (
  user_id text primary key,
  points int not null default 0,
  updated_at timestamptz not null default now()
);

create table if not exists public.reward_history (
  id bigserial primary key,
  user_id text not null references public.reward_profiles(user_id) on delete cascade,
  activity text not null,
  points_delta int not null,
  created_at timestamptz not null default now()
);

insert into public.reward_profiles (user_id, points)
values ('Admin', 1000)
on conflict (user_id) do nothing;

create table if not exists public.community_profiles (
  user_id text primary key,
  reputation_points int not null default 0,
  discussions_count int not null default 0,
  events_rsvped_count int not null default 0,
  resources_downloaded_count int not null default 0,
  badges_earned_count int not null default 0,
  updated_at timestamptz not null default now()
);

create table if not exists public.community_events (
  id bigint primary key,
  title text not null,
  event_type text not null,
  category text not null,
  host_name text,
  host_title text,
  start_at timestamptz not null,
  timezone text not null default 'SGT',
  is_online boolean not null default true,
  location text,
  seats_booked int not null default 0,
  seats_total int not null default 0,
  status_label text,
  is_registered boolean not null default false,
  reminder_set boolean not null default false,
  action_label text,
  created_at timestamptz not null default now()
);

create table if not exists public.community_resources (
  id bigserial primary key,
  title text not null,
  author text not null,
  user_id text not null default 'seed_admin',
  summary text not null,
  tag_primary text,
  tag_secondary text,
  file_type text,
  file_name text,
  file_path text,
  file_url text,
  file_size bigint not null default 0,
  download_count int not null default 0,
  points_reward int not null default 0,
  created_at timestamptz not null default now()
);

alter table public.community_resources
  add column if not exists user_id text default 'seed_admin',
  add column if not exists file_name text,
  add column if not exists file_path text,
  add column if not exists file_url text,
  add column if not exists file_size bigint default 0;

do $$
begin
  if not exists (
    select 1
    from information_schema.columns
    where table_schema = 'public'
      and table_name = 'community_resources'
      and column_name = 'id'
      and is_identity = 'YES'
  ) then
    alter table public.community_resources
      alter column id add generated by default as identity;
  end if;
end $$;

update public.community_resources
set file_size = 0
where file_size is null;

update public.community_resources
set user_id = 'seed_admin'
where user_id is null or btrim(user_id) = '';

alter table public.community_resources
  alter column user_id set not null;

create table if not exists public.community_badges (
  id bigint primary key,
  user_id text not null references public.community_profiles(user_id),
  title text not null,
  description text not null,
  status text not null,
  points int not null default 0,
  earned_at date,
  progress_current int,
  progress_target int,
  progress_percent int,
  icon text,
  icon_color text,
  created_at timestamptz not null default now()
);

insert into public.community_profiles
  (user_id, reputation_points, discussions_count, events_rsvped_count, resources_downloaded_count, badges_earned_count)
values
  ('Admin', 1250, 2, 1, 0, 2)
on conflict (user_id) do nothing;

insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values
  (1001, 'Webinar: Cash Flow Forecasting Mastery', 'WEBINAR', 'Finance', 'Sarah Chen', 'CFO & Financial Consultant', '2025-12-15 14:00:00+08', 'SGT', true, 'Online via Zoom', 67, 100, null, false, false, 'RSVP'),
  (1002, 'Workshop: Digital Marketing on a Budget', 'WORKSHOP', 'Marketing', 'Michael Rodriguez', 'Marketing Expert', '2025-12-20 10:00:00+08', 'SGT', false, 'Mastercard Innovation Center, Singapore', 38, 50, 'You''re Registered', true, true, 'Cancel RSVP')
on conflict (id) do nothing;

insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, download_count, points_reward)
values
  (2001, 'Cash Flow Template & Calculator', 'Admin Team', 'Excel template for tracking and forecasting cash flow', 'Templates', 'XLSX', 'Spreadsheet', 342, 5),
  (2002, 'Invoice Management Best Practices Guide', 'Sarah Chen', 'Comprehensive PDF guide on invoice optimization', 'Guides', 'PDF', 'PDF', 267, 5),
  (2003, 'Budget Planning Spreadsheet', 'Michael Tan', 'Annual budget planning tool with variance tracking', 'Templates', 'XLSX', 'Spreadsheet', 198, 5)
on conflict (id) do nothing;

insert into public.community_badges
  (id, user_id, title, description, status, points, earned_at, progress_current, progress_target, progress_percent, icon, icon_color)
values
  (3001, 'Admin', 'Rising Star', 'Reach 500 reputation points', 'earned', 100, '2026-02-08', null, null, null, 'bi-sun', 'text-warning'),
  (3002, 'Admin', 'Community Leader', 'Reach 1000 reputation points', 'earned', 150, '2026-02-08', null, null, null, 'bi-crown', 'text-warning'),
  (3003, 'Admin', 'Super Contributor', 'Reach 2000 reputation points', 'in_progress', 250, null, 1250, 2000, 63, 'bi-trophy', 'text-warning'),
  (3004, 'Admin', 'Event Enthusiast', 'RSVP to 3 events', 'in_progress', 30, null, 1, 3, 33, 'bi-ticket-perforated', 'text-warning')
on conflict (id) do nothing;

create table if not exists public.community_threads (
  id bigint primary key,
  category text not null,
  title text not null,
  content text not null,
  author text not null,
  user_id text not null default 'seed_admin',
  author_reputation int not null default 0,
  excerpt text,
  tags text,
  upvotes int not null default 0,
  view_count int not null default 0,
  created_at timestamptz not null default now()
);

alter table public.community_threads
  drop column if exists is_solved;

alter table public.community_threads
  add column if not exists user_id text default 'seed_admin';

update public.community_threads
set user_id = 'seed_admin'
where user_id is null or btrim(user_id) = '';

alter table public.community_threads
  alter column user_id set not null;

create table if not exists public.community_thread_replies (
  id bigserial primary key,
  thread_id bigint not null references public.community_threads(id) on delete cascade,
  author text not null,
  user_id text not null default 'seed_admin',
  message text not null,
  created_at timestamptz not null default now()
);

do $$
begin
  if not exists (
    select 1
    from information_schema.columns
    where table_schema = 'public'
      and table_name = 'community_threads'
      and column_name = 'id'
      and is_identity = 'YES'
  ) then
    execute 'create sequence if not exists public.community_threads_id_seq';
    execute 'alter table public.community_threads alter column id set default nextval(''public.community_threads_id_seq'')';
    execute 'alter sequence public.community_threads_id_seq owned by public.community_threads.id';
  end if;
end $$;

alter table public.community_thread_replies
  add column if not exists user_id text default 'seed_admin';

update public.community_thread_replies
set user_id = 'seed_admin'
where user_id is null or btrim(user_id) = '';

alter table public.community_thread_replies
  alter column user_id set not null;

create table if not exists public.community_thread_votes (
  id bigserial primary key,
  thread_id bigint not null references public.community_threads(id) on delete cascade,
  user_id text not null,
  created_at timestamptz not null default now(),
  unique (thread_id, user_id)
);

create table if not exists public.community_thread_views (
  id bigserial primary key,
  thread_id bigint not null references public.community_threads(id) on delete cascade,
  user_id text not null,
  created_at timestamptz not null default now(),
  unique (thread_id, user_id)
);

do $$
begin
  if not exists (
    select 1
    from information_schema.columns
    where table_schema = 'public'
      and table_name = 'community_thread_replies'
      and column_name = 'id'
      and is_identity = 'YES'
  ) then
    execute 'create sequence if not exists public.community_thread_replies_id_seq';
    execute 'alter table public.community_thread_replies alter column id set default nextval(''public.community_thread_replies_id_seq'')';
    execute 'alter sequence public.community_thread_replies_id_seq owned by public.community_thread_replies.id';
  end if;
end $$;

do $$
begin
  if not exists (
    select 1
    from information_schema.columns
    where table_schema = 'public'
      and table_name = 'community_thread_votes'
      and column_name = 'id'
      and is_identity = 'YES'
  ) then
    execute 'create sequence if not exists public.community_thread_votes_id_seq';
    execute 'alter table public.community_thread_votes alter column id set default nextval(''public.community_thread_votes_id_seq'')';
    execute 'alter sequence public.community_thread_votes_id_seq owned by public.community_thread_votes.id';
  end if;
end $$;

create table if not exists public.community_resource_downloads (
  id bigserial primary key,
  resource_id bigint not null references public.community_resources(id) on delete cascade,
  user_id text not null,
  downloaded_at timestamptz not null default now(),
  unique (resource_id, user_id)
);

create table if not exists public.community_event_rsvps (
  id bigserial primary key,
  event_id bigint not null references public.community_events(id) on delete cascade,
  user_id text not null,
  reminder_set boolean not null default true,
  created_at timestamptz not null default now(),
  unique (event_id, user_id)
);

insert into public.community_threads
  (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values
  (4001, 'Cash Flow', 'Best practices for managing seasonal cash flow?', 'Running a retail business and struggling with seasonal variations in payments. Looking for tips to manage the gaps.', 'Sarah Chen', 2450, 'Running a retail business and struggling with seasonal variations...', 'cash-flow, retail, seasonal', 24, 156, '2026-02-08 05:50:00+08'),
  (4002, 'Credit Management', 'Understanding business credit reports', 'Can someone explain how to read and interpret business credit reports and what lenders look for?', 'James Lim', 1800, 'Can someone explain how to read and interpret business credit reports?', 'credit-score, reports, analysis', 21, 142, '2026-02-08 05:52:00+08')
on conflict (id) do nothing;

insert into public.community_thread_replies
  (id, thread_id, author, message, created_at)
values
  (4101, 4001, 'Michael Tan', 'We built a 13-week rolling forecast and negotiated net-45 terms with suppliers. Helped stabilize cash swings.', '2026-02-08 06:10:00+08'),
  (4102, 4001, 'Admin', 'Consider building a cash buffer and setting early-payment incentives for customers.', '2026-02-08 06:15:00+08'),
  (4103, 4002, 'Admin', 'Focus on payment history, utilization, and public filings. Use the report trends rather than just the score.', '2026-02-08 06:20:00+08')
on conflict (id) do nothing;

grant select, insert, update, delete on table public.community_profiles to anon, authenticated;
grant select, insert, update, delete on table public.community_events to anon, authenticated;
grant select, insert, update, delete on table public.community_resources to anon, authenticated;
grant select, insert, update, delete on table public.community_badges to anon, authenticated;
grant select, insert, update, delete on table public.community_threads to anon, authenticated;
grant select, insert, update, delete on table public.community_thread_replies to anon, authenticated;
grant select, insert, update, delete on table public.community_thread_votes to anon, authenticated;
grant select, insert, update, delete on table public.community_thread_views to anon, authenticated;
grant select, insert, update, delete on table public.community_resource_downloads to anon, authenticated;
grant select, insert, update, delete on table public.community_event_rsvps to anon, authenticated;

-- =========================
-- SEED: TOPICS
-- =========================
insert into public.learning_topics (id, category, title, summary, estimated_minutes) values
  (1, 'Finance Basics', 'Personal Budgeting', 'Learn how to manage income, expenses, and savings effectively.', 20),
  (2, 'Credit & Loans', 'Understanding Credit', 'Understand credit scores, loans, and responsible borrowing.', 25),
  (3, 'Cashflow', 'Cashflow Management', 'Master cashflow timing, cycles, and forecasting.', 25),
  (4, 'Operations', 'Cost Control & Efficiency', 'Improve margins through smarter operations.', 20),
  (5, 'Sales', 'Pricing & Revenue', 'Build pricing strategy and grow revenue sustainably.', 25)
on conflict (id) do nothing;

-- =========================
-- SEED: MODULES (3 levels each)
-- =========================
insert into public.learning_modules (id, topic_id, difficulty, title) values
  (101, 1, 0, 'Budgeting 101'),
  (102, 1, 1, 'Budgeting 201'),
  (103, 1, 2, 'Budgeting Mastery'),

  (201, 2, 0, 'Credit Basics'),
  (202, 2, 1, 'Credit Strategy'),
  (203, 2, 2, 'Advanced Credit Optimization'),

  (301, 3, 0, 'Cashflow 101'),
  (302, 3, 1, 'Cashflow Strategy'),
  (303, 3, 2, 'Advanced Cashflow Planning'),

  (401, 4, 0, 'Cost Control 101'),
  (402, 4, 1, 'Operational Efficiency'),
  (403, 4, 2, 'Advanced Cost Optimization'),

  (501, 5, 0, 'Pricing 101'),
  (502, 5, 1, 'Pricing Strategy'),
  (503, 5, 2, 'Advanced Revenue Growth')
on conflict (id) do nothing;

-- =========================
-- SEED: SECTIONS (existing content)
-- =========================
insert into public.learning_sections (id, module_id, "order", heading, body) values
  (1001, 101, 1, 'Budget Foundations', 'List income sources and fixed bills to set a simple baseline.'),
  (1002, 101, 2, 'Basic Category Limits', 'Set simple caps for needs, wants, and savings to guide daily spending.'),
  (1003, 101, 3, 'Expense Tracking', 'Record every transaction for one week to reveal your real spending patterns.'),
  (1004, 101, 4, 'Budget Review', 'Compare your plan to actuals at month-end and note two changes for next month.'),

  (1101, 102, 1, 'Zero-Based Allocation', 'Plan every dollar to a category before the month begins.'),
  (1102, 102, 2, 'Variance Review', 'Compare plan vs actual weekly and reallocate early when off track.'),
  (1103, 102, 3, 'Sinking Funds', 'Save small amounts monthly for annual costs like insurance or taxes.'),
  (1104, 102, 4, 'Budget Buffers', 'Add a small buffer to categories that often run over.'),

  (1201, 103, 1, 'Scenario Budgeting', 'Build base, downside, and severe cases to protect runway.'),
  (1202, 103, 2, 'Constraint Prioritization', 'Lock essentials first, then optimize discretionary and growth spend.'),
  (1203, 103, 3, 'Runway Targets', 'Set a minimum months-of-cash target and track progress monthly.'),
  (1204, 103, 4, 'Capital Allocation', 'Choose between growth, debt reduction, and reserves based on ROI.'),

  (2001, 201, 1, 'Credit Basics', 'Understand borrowing, interest, and why repayment history matters.'),
  (2002, 201, 2, 'Score Factors', 'Learn the key inputs: payment history, utilization, and account age.'),
  (2003, 201, 3, 'Interest Costs', 'Understand how APR impacts total repayment over time.'),
  (2004, 201, 4, 'Credit Reports', 'Learn how to check reports and spot errors.'),

  (2101, 202, 1, 'Utilization Management', 'Reduce statement balances and keep limits under control.'),
  (2102, 202, 2, 'Account Mix Strategy', 'Balance credit cards and installment loans without opening too fast.'),
  (2103, 202, 3, 'Payment Timing', 'Pay before statements close to improve utilization.'),
  (2104, 202, 4, 'Credit Growth', 'Request increases only when usage and payments are stable.'),

  (2201, 203, 1, 'Negotiation Preparation', 'Use financial ratios, collateral, and cashflow evidence.'),
  (2202, 203, 2, 'Refi Economics', 'Calculate total savings after fees and term changes.'),
  (2203, 203, 3, 'Covenant Awareness', 'Understand covenants and how to avoid breaches.'),
  (2204, 203, 4, 'Risk Pricing', 'Learn how lenders price risk and what improves terms.'),

  (3001, 301, 1, 'Inflow vs Outflow', 'Track when cash enters and leaves, not just total profit.'),
  (3002, 301, 2, 'Cash Timing', 'Understand how invoice timing creates short-term shortages.'),
  (3003, 301, 3, 'Collections Basics', 'Set clear payment terms and follow-up schedules.'),
  (3004, 301, 4, 'Payment Scheduling', 'Align bills with expected inflows to avoid crunches.'),

  (3101, 302, 1, 'Cycle Optimization', 'Shorten the cycle by improving DSO and inventory days.'),
  (3102, 302, 2, 'Forecast Discipline', 'Use rolling forecasts with weekly updates and monthly extensions.'),
  (3103, 302, 3, 'Receivables Strategy', 'Prioritize late accounts and automate reminders.'),
  (3104, 302, 4, 'Inventory Controls', 'Set reorder points and reduce slow-moving stock.'),

  (3201, 303, 1, 'Stress Scenarios', 'Test extreme cases like delayed receivables or cost spikes.'),
  (3202, 303, 2, 'Liquidity Architecture', 'Plan reserves, credit lines, and seasonal buffers together.'),
  (3203, 303, 3, 'Capital Stacking', 'Combine internal cash, credit, and external funding strategically.'),
  (3204, 303, 4, 'Early Warning Signals', 'Track KPIs that predict cash shortfalls.'),

  (4001, 401, 1, 'Cost Types', 'Separate fixed vs variable costs to understand margin drivers.'),
  (4002, 401, 2, 'Waste Awareness', 'Identify rework, waiting time, and unnecessary steps.'),
  (4003, 401, 3, 'Spend Visibility', 'Group expenses by category to spot the biggest levers.'),
  (4004, 401, 4, 'Quick Wins', 'Target small, low-risk reductions first.'),

  (4101, 402, 1, 'Unit Economics', 'Track contribution margin per product or service.'),
  (4102, 402, 2, 'Workflow Standards', 'Define standard processes before adding automation.'),
  (4103, 402, 3, 'Bottleneck Analysis', 'Find steps that slow delivery and measure impact.'),
  (4104, 402, 4, 'Cost-to-Serve', 'Include support and delivery costs in decisions.'),

  (4201, 403, 1, 'Supplier Strategy', 'Use spend data and competitive bids to negotiate.'),
  (4202, 403, 2, 'Continuous Improvement', 'Use KPIs to drive ongoing cost and quality gains.'),
  (4203, 403, 3, 'Demand Planning', 'Use forecasts to negotiate better pricing and lead times.'),
  (4204, 403, 4, 'Process Controls', 'Add checks that prevent costly defects upstream.'),

  (5001, 501, 1, 'Pricing Fundamentals', 'Set prices using costs, value, and competitive context.'),
  (5002, 501, 2, 'Revenue Mechanics', 'Link price changes to quantity, margins, and revenue mix.'),
  (5003, 501, 3, 'Price Testing', 'Test small price changes and measure conversion impact.'),
  (5004, 501, 4, 'Positioning', 'Align pricing with the value story in your marketing.'),

  (5101, 502, 1, 'Tiered Offers', 'Design tiers that match customer willingness-to-pay.'),
  (5102, 502, 2, 'Packaging Strategy', 'Bundle features to increase perceived value and upgrades.'),
  (5103, 502, 3, 'Anchor Pricing', 'Use a premium tier to frame mid-tier value.'),
  (5104, 502, 4, 'Discount Policies', 'Set clear rules for when discounts are allowed.'),

  (5201, 503, 1, 'Expansion Growth', 'Use upsells and cross-sells driven by product usage.'),
  (5202, 503, 2, 'Retention Metrics', 'Optimize NRR and churn drivers with targeted improvements.'),
  (5203, 503, 3, 'Lifecycle Messaging', 'Use onboarding and lifecycle touches to improve retention.'),
  (5204, 503, 4, 'Churn Prevention', 'Identify cancellation signals early and respond fast.')
on conflict (id) do nothing;

-- ============================================================
-- QUIZ POOL: EXISTING QUESTIONS (2 per module)
-- ============================================================
insert into public.learning_quiz_questions (id, learning_module_id, question) values
  -- Budgeting 101
  (5001, 101, 'Which is a common budgeting rule?'),
  (5002, 101, 'In 50/30/20, what does the 20% usually represent?'),

  -- Credit Basics
  (6001, 201, 'What is credit?'),
  (6002, 201, 'What helps improve a credit score?'),

  -- Cashflow 101
  (7001, 301, 'Cashflow measures:'),
  (7002, 301, 'Why do timing gaps matter?'),

  -- Cost Control 101
  (8001, 401, 'Fixed costs are:'),
  (8002, 401, 'Operational waste means:'),

  -- Pricing 101
  (9001, 501, 'Value-based pricing depends on:'),
  (9002, 501, 'A diversified revenue mix helps:'),

  -- Budgeting 201
  (5101, 102, 'What is the goal of zero-based budgeting?'),
  (5102, 102, 'What is a sinking fund used for?'),

  -- Budgeting Mastery
  (5201, 103, 'What is a cash buffer?'),
  (5202, 103, 'Why use scenario planning?'),

  -- Credit Strategy
  (6101, 202, 'Ideal credit utilization is generally below?'),
  (6102, 202, 'Why does credit mix matter?'),

  -- Advanced Credit Optimization
  (6201, 203, 'When should you negotiate rates?'),
  (6202, 203, 'A good reason to refinance is:'),

  -- Cashflow Strategy
  (7101, 302, 'The cash conversion cycle measures:'),
  (7102, 302, 'Why review forecasts weekly?'),

  -- Advanced Cashflow Planning
  (7201, 303, 'Sensitivity analysis helps you:'),
  (7202, 303, 'Liquidity planning ensures:'),

  -- Operational Efficiency
  (8101, 402, 'Unit economics tells you:'),
  (8102, 402, 'Automation is best for:'),

  -- Advanced Cost Optimization
  (8201, 403, 'Renegotiating suppliers can:'),
  (8202, 403, 'Lean operations focus on:'),

  -- Pricing Strategy
  (9101, 502, 'Price ladders are:'),
  (9102, 502, 'Discount discipline means:'),

  -- Advanced Revenue Growth
  (9201, 503, 'Expansion revenue comes from:'),
  (9202, 503, 'Retention improves:')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
  -- 5001
  (9001, 5001, '50/30/20 rule', true),
  (9002, 5001, '80/10/10 rule', false),
  (9003, 5001, '100% spending rule', false),

  -- 5002
  (9011, 5002, 'Savings / debt repayment', true),
  (9012, 5002, 'Entertainment only', false),
  (9013, 5002, 'Rent only', false),

  -- 6001
  (9101, 6001, 'Borrowed money you must repay', true),
  (9102, 6001, 'Free money from banks', false),
  (9103, 6001, 'A type of insurance', false),

  -- 6002
  (9111, 6002, 'Paying on time', true),
  (9112, 6002, 'Missing payments', false),
  (9113, 6002, 'Maxing every credit card', false),

  -- 7001
  (9121, 7001, 'Money in and out over time', true),
  (9122, 7001, 'Only profits', false),
  (9123, 7001, 'Only expenses', false),

  -- 7002
  (9131, 7002, 'They can cause cash shortages', true),
  (9132, 7002, 'They increase profits', false),
  (9133, 7002, 'They do not matter', false),

  -- 8001
  (9141, 8001, 'Costs that do not change with volume', true),
  (9142, 8001, 'Costs that vary with sales', false),
  (9143, 8001, 'Optional costs only', false),

  -- 8002
  (9151, 8002, 'Steps that add no value', true),
  (9152, 8002, 'Necessary compliance work', false),
  (9153, 8002, 'Customer feedback', false),

  -- 9001
  (9161, 9001, 'Customer perceived value', true),
  (9162, 9001, 'Competitor price only', false),
  (9163, 9001, 'Cost plus only', false),

  -- 9002
  (9171, 9002, 'Reduce dependence on one source', true),
  (9172, 9002, 'Guarantee higher margins', false),
  (9173, 9002, 'Remove all risk', false),

  -- 5101
  (9201, 5101, 'Assign every dollar a purpose', true),
  (9202, 5101, 'Spend without tracking', false),
  (9203, 5101, 'Only track large expenses', false),

  -- 5102
  (9211, 5102, 'Save gradually for planned expenses', true),
  (9212, 5102, 'Borrow for emergencies', false),
  (9213, 5102, 'Ignore future costs', false),

  -- 5201
  (9221, 5201, 'Cash set aside for short-term shocks', true),
  (9222, 5201, 'A line of credit', false),
  (9223, 5201, 'Unpaid invoices', false),

  -- 5202
  (9231, 5202, 'Prepare for changing conditions', true),
  (9232, 5202, 'Avoid forecasting', false),
  (9233, 5202, 'Increase risk', false),

  -- 6101
  (9241, 6101, '30%', true),
  (9242, 6101, '80%', false),
  (9243, 6101, '100%', false),

  -- 6102
  (9251, 6102, 'Shows ability to handle different credit types', true),
  (9252, 6102, 'It does not matter', false),
  (9253, 6102, 'Only cash matters', false),

  -- 6201
  (9261, 6201, 'When finances improve and risk is lower', true),
  (9262, 6201, 'When missing payments', false),
  (9263, 6201, 'When revenue is falling sharply', false),

  -- 6202
  (9271, 6202, 'Lower rate or better terms available', true),
  (9272, 6202, 'To increase payments', false),
  (9273, 6202, 'To extend debt without planning', false),

  -- 7101
  (9281, 7101, 'Days cash is tied in operations', true),
  (9282, 7101, 'Total annual revenue', false),
  (9283, 7101, 'Only inventory days', false),

  -- 7102
  (9291, 7102, 'To adjust for actual performance', true),
  (9292, 7102, 'To avoid forecasting', false),
  (9293, 7102, 'To increase debt', false),

  -- 7201
  (9301, 7201, 'Understand impact of changes', true),
  (9302, 7201, 'Eliminate planning', false),
  (9303, 7201, 'Guarantee profits', false),

  -- 7202
  (9311, 7202, 'Cash is available when needed', true),
  (9312, 7202, 'Costs are fixed forever', false),
  (9313, 7202, 'Credit is never used', false),

  -- 8101
  (9321, 8101, 'Profitability per unit', true),
  (9322, 8101, 'Total company headcount', false),
  (9323, 8101, 'Tax rate only', false),

  -- 8102
  (9331, 8102, 'Repeatable, low-judgment tasks', true),
  (9332, 8102, 'One-off strategic decisions', false),
  (9333, 8102, 'Customer interviews', false),

  -- 8201
  (9341, 8201, 'Lower costs or improve terms', true),
  (9342, 8201, 'Increase waste', false),
  (9343, 8201, 'Reduce quality', false),

  -- 8202
  (9351, 8202, 'Reducing waste and variability', true),
  (9352, 8202, 'Adding steps for safety', false),
  (9353, 8202, 'Increasing inventory', false),

  -- 9101
  (9361, 9101, 'Tiered pricing options', true),
  (9362, 9101, 'Single price forever', false),
  (9363, 9101, 'Discount-only pricing', false),

  -- 9102
  (9371, 9102, 'Use discounts with clear goals', true),
  (9372, 9102, 'Always discount first', false),
  (9373, 9102, 'Avoid tracking discounts', false),

  -- 9201
  (9381, 9201, 'Upsells and cross-sells', true),
  (9382, 9201, 'Only new customers', false),
  (9383, 9201, 'Reducing prices', false),

  -- 9202
  (9391, 9202, 'Lifetime value and margin', true),
  (9392, 9202, 'Churn and refunds', false),
  (9393, 9202, 'Only acquisition costs', false)
on conflict (id) do nothing;

-- ============================================================
-- ADDITIONAL QUESTIONS: +8 PER MODULE (to reach 10 per module)
-- ============================================================

-- ========= Budgeting 101 (101): add 8 =========
insert into public.learning_quiz_questions (id, learning_module_id, question) values
(101003, 101, 'Which expense is usually a "need" in budgeting?'),
(101004, 101, 'What is the main purpose of tracking expenses?'),
(101005, 101, 'If you overspend on "wants", what is the best immediate action?'),
(101006, 101, 'A budget is best described as:'),
(101007, 101, 'Which is an example of a variable expense?'),
(101008, 101, 'What is a realistic budgeting frequency for beginners?'),
(101009, 101, 'Which habit supports better budgeting results?'),
(101010, 101, 'What is the simplest first step to start budgeting?')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
(10100301, 101003, 'Rent / housing', true),
(10100302, 101003, 'Concert tickets', false),
(10100303, 101003, 'Luxury shopping', false),
(10100401, 101004, 'To understand where money is going', true),
(10100402, 101004, 'To increase income automatically', false),
(10100403, 101004, 'To eliminate all spending', false),
(10100501, 101005, 'Reduce wants and rebalance categories', true),
(10100502, 101005, 'Stop paying bills', false),
(10100503, 101005, 'Ignore it and continue', false),
(10100601, 101006, 'A plan for spending and saving', true),
(10100602, 101006, 'A guarantee of profits', false),
(10100603, 101006, 'A bank loan requirement only', false),
(10100701, 101007, 'Utilities (may change monthly)', true),
(10100702, 101007, 'Fixed monthly rent', false),
(10100703, 101007, 'A one-time annual fee only', false),
(10100801, 101008, 'Weekly review', true),
(10100802, 101008, 'Every 5 years', false),
(10100803, 101008, 'Never review to avoid stress', false),
(10100901, 101009, 'Recording spending consistently', true),
(10100902, 101009, 'Only tracking big purchases', false),
(10100903, 101009, 'Tracking only when money is low', false),
(10101001, 101010, 'List income and top expenses', true),
(10101002, 101010, 'Invest everything immediately', false),
(10101003, 101010, 'Cancel all subscriptions forever', false)
on conflict (id) do nothing;

-- ========= Budgeting 201 (102): add 8 =========
insert into public.learning_quiz_questions (id, learning_module_id, question) values
(102003, 102, 'Zero-based budgeting means your ending balance should be:'),
(102004, 102, 'A sinking fund is MOST useful for:'),
(102005, 102, 'Which is an example of a sinking fund category?'),
(102006, 102, 'What is a common benefit of zero-based budgeting?'),
(102007, 102, 'If your income is irregular, a good budgeting approach is:'),
(102008, 102, 'What is the best way to prevent �category leakage�?'),
(102009, 102, 'Sinking funds help reduce reliance on:'),
(102010, 102, 'Which tool best supports zero-based budgeting?')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
(10200301, 102003, 'Zero (every dollar assigned)', true),
(10200302, 102003, 'Whatever is left over randomly', false),
(10200303, 102003, 'Negative to maximize borrowing', false),
(10200401, 102004, 'Planned future expenses', true),
(10200402, 102004, 'Daily groceries', false),
(10200403, 102004, 'Speculative investing only', false),
(10200501, 102005, 'Annual insurance premium', true),
(10200502, 102005, 'Daily transport fare', false),
(10200503, 102005, 'Free gifts', false),
(10200601, 102006, 'Better spending awareness and control', true),
(10200602, 102006, 'Guaranteed higher salary', false),
(10200603, 102006, 'No need to track receipts', false),
(10200701, 102007, 'Budget from a conservative baseline income', true),
(10200702, 102007, 'Spend first and see later', false),
(10200703, 102007, 'Assume best-case income always', false),
(10200801, 102008, 'Set limits and log spending immediately', true),
(10200802, 102008, 'Review once a year only', false),
(10200803, 102008, 'Use multiple overlapping budgets', false),
(10200901, 102009, 'Debt or emergency borrowing', true),
(10200902, 102009, 'Income taxes', false),
(10200903, 102009, 'Earning interest', false),
(10201001, 102010, 'A category-based tracker (app/sheet)', true),
(10201002, 102010, 'A lottery ticket', false),
(10201003, 102010, 'A social media feed', false)
on conflict (id) do nothing;

-- ========= Budgeting Mastery (103): add 8 =========
insert into public.learning_quiz_questions (id, learning_module_id, question) values
(103003, 103, 'A 3�6 month cash buffer is mainly used to handle:'),
(103004, 103, 'Scenario planning is MOST useful when:'),
(103005, 103, 'In worst-case budgeting, your priority is to protect:'),
(103006, 103, 'Which is a good example of stress-testing a budget?'),
(103007, 103, 'A strong liquidity buffer reduces the need for:'),
(103008, 103, 'What is a practical way to build a cash buffer?'),
(103009, 103, 'Scenario planning should typically include:'),
(103010, 103, 'The best measure of buffer adequacy is based on:')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
(10300301, 103003, 'Short-term shocks (income drop, emergencies)', true),
(10300302, 103003, 'Luxury spending upgrades', false),
(10300303, 103003, 'Increasing debt limits', false),
(10300401, 103004, 'Conditions are uncertain or changing', true),
(10300402, 103004, 'Everything is perfectly stable', false),
(10300403, 103004, 'You refuse to update budgets', false),
(10300501, 103005, 'Essential needs and solvency', true),
(10300502, 103005, 'Wants and entertainment', false),
(10300503, 103005, 'Impulse purchases', false),
(10300601, 103006, 'Model a 20% revenue drop for 3 months', true),
(10300602, 103006, 'Assume revenue doubles instantly', false),
(10300603, 103006, 'Ignore fixed costs', false),
(10300701, 103007, 'High-interest emergency borrowing', true),
(10300702, 103007, 'Tracking expenses', false),
(10300703, 103007, 'Saving money', false),
(10300801, 103008, 'Automate transfers to a buffer account', true),
(10300802, 103008, 'Wait for a bonus only', false),
(10300803, 103008, 'Invest 100% with no cash', false),
(10300901, 103009, 'Best/base/worst assumptions', true),
(10300902, 103009, 'Only best-case assumptions', false),
(10300903, 103009, 'No assumptions', false),
(10301001, 103010, 'Monthly essential costs', true),
(10301002, 103010, 'Number of credit cards', false),
(10301003, 103010, 'How many subscriptions you have', false)
on conflict (id) do nothing;

-- ========= Credit Basics (201): add 8 =========
insert into public.learning_quiz_questions (id, learning_module_id, question) values
(201003, 201, 'Interest is best described as:'),
(201004, 201, 'Which action usually hurts your credit score?'),
(201005, 201, 'A loan term refers to:'),
(201006, 201, 'Why do lenders check credit score?'),
(201007, 201, 'A minimum payment on a credit card means:'),
(201008, 201, 'Which is a responsible borrowing habit?'),
(201009, 201, 'Credit limit is:'),
(201010, 201, 'Which statement about debt is true?')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
(20100301, 201003, 'Cost of borrowing money', true),
(20100302, 201003, 'Free reward points', false),
(20100303, 201003, 'A tax refund', false),
(20100401, 201004, 'Missing payments', true),
(20100402, 201004, 'Paying on time', false),
(20100403, 201004, 'Keeping utilization low', false),
(20100501, 201005, 'How long you have to repay', true),
(20100502, 201005, 'Your credit card PIN', false),
(20100503, 201005, 'Your monthly salary', false),
(20100601, 201006, 'To estimate repayment risk', true),
(20100602, 201006, 'To decide your hobbies', false),
(20100603, 201006, 'To set your rent', false),
(20100701, 201007, 'Smallest amount to keep account current', true),
(20100702, 201007, 'Full repayment with no interest', false),
(20100703, 201007, 'A reward payout', false),
(20100801, 201008, 'Borrow within ability to repay', true),
(20100802, 201008, 'Borrow the maximum always', false),
(20100803, 201008, 'Ignore repayment dates', false),
(20100901, 201009, 'Max amount you can borrow on that account', true),
(20100902, 201009, 'Your bank balance', false),
(20100903, 201009, 'Your monthly bill', false),
(20101001, 201010, 'Debt can be useful if managed responsibly', true),
(20101002, 201010, 'Debt is always good no matter what', false),
(20101003, 201010, 'Debt never requires repayment', false)
on conflict (id) do nothing;

-- ========= Credit Strategy (202): add 8 =========
insert into public.learning_quiz_questions (id, learning_module_id, question) values
(202003, 202, 'Keeping utilization below 30% generally helps because:'),
(202004, 202, 'Which behavior best supports strong credit utilization?'),
(202005, 202, 'Credit mix refers to having:'),
(202006, 202, 'Which is an example of different credit types?'),
(202007, 202, 'A good strategy to reduce utilization quickly is to:'),
(202008, 202, 'Utilization is calculated as:'),
(202009, 202, 'Opening too many new accounts quickly may:'),
(202010, 202, 'A healthy credit profile is MOST supported by:')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
(20200301, 202003, 'It signals lower reliance on credit', true),
(20200302, 202003, 'It guarantees loan approval', false),
(20200303, 202003, 'It removes interest charges', false),
(20200401, 202004, 'Pay balances down before statement date', true),
(20200402, 202004, 'Max out cards monthly', false),
(20200403, 202004, 'Skip payments', false),
(20200501, 202005, 'Different types of credit accounts', true),
(20200502, 202005, 'Only one credit card brand', false),
(20200503, 202005, 'Only cash transactions', false),
(20200601, 202006, 'Credit card and installment loan', true),
(20200602, 202006, 'Two identical credit cards', false),
(20200603, 202006, 'Two debit cards', false),
(20200701, 202007, 'Pay down balances or increase limit responsibly', true),
(20200702, 202007, 'Spend more to earn points', false),
(20200703, 202007, 'Ignore utilization', false),
(20200801, 202008, 'Balance � credit limit', true),
(20200802, 202008, 'Income � rent', false),
(20200803, 202008, 'Loan term � interest rate', false),
(20200901, 202009, 'Lower your score temporarily', true),
(20200902, 202009, 'Always improve your score', false),
(20200903, 202009, 'Eliminate repayment risk', false),
(20201001, 202010, 'On-time payments plus controlled utilization', true),
(20201002, 202010, 'Only high credit limits', false),
(20201003, 202010, 'Only having many accounts', false)
on conflict (id) do nothing;

-- ========= Advanced Credit Optimization (203): add 8 =========
insert into public.learning_quiz_questions (id, learning_module_id, question) values
(203003, 203, 'Rate negotiation is easier when you can show:'),
(203004, 203, 'Refinancing is MOST beneficial when:'),
(203005, 203, 'A key refinancing risk is:'),
(203006, 203, 'Which factor usually improves negotiating power?'),
(203007, 203, 'When refinancing, you should compare:'),
(203008, 203, 'A good reason NOT to refinance is:'),
(203009, 203, 'Improving cashflow can help you negotiate because:'),
(203010, 203, 'The best refinancing decision metric is often:')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
(20300301, 203003, 'Stable income and strong repayment history', true),
(20300302, 203003, 'Frequent missed payments', false),
(20300303, 203003, 'No documentation', false),
(20300401, 203004, 'Total cost decreases with better terms', true),
(20300402, 203004, 'Rates are higher than before', false),
(20300403, 203004, 'You want more penalties', false),
(20300501, 203005, 'Fees outweighing savings', true),
(20300502, 203005, 'Guaranteed profit', false),
(20300503, 203005, 'No change in obligations', false),
(20300601, 203006, 'Low debt-to-income and good score', true),
(20300602, 203006, 'High utilization and late payments', false),
(20300603, 203006, 'No credit history', false),
(20300701, 203007, 'APR, fees, term, and total repayment', true),
(20300702, 203007, 'Only monthly payment size', false),
(20300703, 203007, 'Only lender brand', false),
(20300801, 203008, 'Early repayment penalties make it expensive', true),
(20300802, 203008, 'Lower interest rate is available', false),
(20300803, 203008, 'Better terms are offered', false),
(20300901, 203009, 'It reduces lender risk perception', true),
(20300902, 203009, 'It removes the need to repay', false),
(20300903, 203009, 'It increases penalties', false),
(20301001, 203010, 'Total savings after fees over time', true),
(20301002, 203010, 'Number of emails from lender', false),
(20301003, 203010, 'Loan color/theme', false)
on conflict (id) do nothing;

-- ========= Cashflow 101 (301): add 8 =========
insert into public.learning_quiz_questions (id, learning_module_id, question) values
(301003, 301, 'Profit differs from cashflow mainly because of:'),
(301004, 301, 'A cash shortage happens when:'),
(301005, 301, 'Which is an example of cash inflow?'),
(301006, 301, 'Which is an example of cash outflow?'),
(301007, 301, 'A common cause of timing gaps is:'),
(301008, 301, 'Why is tracking cashflow weekly useful?'),
(301009, 301, 'Which statement is true?'),
(301010, 301, 'The simplest cashflow view is:')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
(30100301, 301003, 'Timing of payments and receipts', true),
(30100302, 301003, 'Cashflow is always higher than profit', false),
(30100303, 301003, 'Profit is always cash in hand', false),
(30100401, 301004, 'Cash out exceeds cash in at that time', true),
(30100402, 301004, 'Revenue is high', false),
(30100403, 301004, 'Expenses are zero', false),
(30100501, 301005, 'Customer payment received', true),
(30100502, 301005, 'Rent payment', false),
(30100503, 301005, 'Utility bill', false),
(30100601, 301006, 'Supplier payment', true),
(30100602, 301006, 'Invoice issued (not yet paid)', false),
(30100603, 301006, 'Customer promise to pay', false),
(30100701, 301007, 'Customers pay late', true),
(30100702, 301007, 'You track everything well', false),
(30100703, 301007, 'You receive cash immediately always', false),
(30100801, 301008, 'You spot shortages early', true),
(30100802, 301008, 'It increases profit automatically', false),
(30100803, 301008, 'It removes need for invoices', false),
(30100901, 301009, 'A profitable business can still run out of cash', true),
(30100902, 301009, 'Cashflow does not matter', false),
(30100903, 301009, 'Only profit matters', false),
(30101001, 301010, 'Cash in minus cash out over time', true),
(30101002, 301010, 'Only net profit per year', false),
(30101003, 301010, 'Only total revenue', false)
on conflict (id) do nothing;

-- ========= Cashflow Strategy (302): add 8 =========
insert into public.learning_quiz_questions (id, learning_module_id, question) values
(302003, 302, 'Shortening the cash conversion cycle typically means:'),
(302004, 302, 'To free up working capital, you can:'),
(302005, 302, 'A weekly forecast cadence helps because:'),
(302006, 302, 'Which action improves receivables?'),
(302007, 302, 'Which action improves payables strategy safely?'),
(302008, 302, 'Inventory days are reduced by:'),
(302009, 302, 'Forecast accuracy improves when you:'),
(302010, 302, 'A good cashflow KPI for SMEs is:')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
(30200301, 302003, 'Cash returns faster from operations', true),
(30200302, 302003, 'Cash is locked up longer', false),
(30200303, 302003, 'You stop selling', false),
(30200401, 302004, 'Collect faster and manage inventory tightly', true),
(30200402, 302004, 'Pay suppliers earlier always', false),
(30200403, 302004, 'Increase unnecessary stock', false),
(30200501, 302005, 'You update with actuals and react quickly', true),
(30200502, 302005, 'It guarantees sales growth', false),
(30200503, 302005, 'It removes seasonality', false),
(30200601, 302006, 'Clear invoice terms and follow-up', true),
(30200602, 302006, 'No invoices', false),
(30200603, 302006, 'Longer payment terms for customers', false),
(30200701, 302007, 'Negotiate terms without damaging relationships', true),
(30200702, 302007, 'Never pay suppliers', false),
(30200703, 302007, 'Pay immediately regardless of cash', false),
(30200801, 302008, 'Improving demand planning and turnover', true),
(30200802, 302008, 'Buying more slow stock', false),
(30200803, 302008, 'Stopping stock counts', false),
(30200901, 302009, 'Compare forecast vs actual weekly', true),
(30200902, 302009, 'Ignore actuals', false),
(30200903, 302009, 'Forecast once per year', false),
(30201001, 302010, 'Cash runway (weeks/months of liquidity)', true),
(30201002, 302010, 'Number of office chairs', false),
(30201003, 302010, 'Logo quality', false)
on conflict (id) do nothing;

-- ========= Advanced Cashflow Planning (303): add 8 =========
insert into public.learning_quiz_questions (id, learning_module_id, question) values
(303003, 303, 'Sensitivity analysis answers the question:'),
(303004, 303, 'If receivables are delayed by 30 days, the best response is:'),
(303005, 303, 'Liquidity planning aligns credit lines with:'),
(303006, 303, 'Which is a high-impact scenario variable to test?'),
(303007, 303, 'A liquidity buffer is different from a credit line because it is:'),
(303008, 303, 'For seasonal businesses, planning should focus on:'),
(303009, 303, 'A strong liquidity plan reduces:'),
(303010, 303, 'A good advanced cashflow model includes:')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
(30300301, 303003, 'How outcomes change when inputs change', true),
(30300302, 303003, 'How to avoid budgeting', false),
(30300303, 303003, 'How to increase prices instantly', false),
(30300401, 303004, 'Update forecast and secure short-term liquidity', true),
(30300402, 303004, 'Ignore it and hope', false),
(30300403, 303004, 'Increase non-essential spend', false),
(30300501, 303005, 'Timing and seasonality of cash needs', true),
(30300502, 303005, 'Brand colors', false),
(30300503, 303005, 'Staff birthdays', false),
(30300601, 303006, 'Days sales outstanding (DSO)', true),
(30300602, 303006, 'Office paint color', false),
(30300603, 303006, 'Number of meetings', false),
(30300701, 303007, 'Cash you already have', true),
(30300702, 303007, 'Money you must borrow', false),
(30300703, 303007, 'A guaranteed income', false),
(30300801, 303008, 'Peak vs off-peak cash requirements', true),
(30300802, 303008, 'Random spending', false),
(30300803, 303008, 'Never updating forecasts', false),
(30300901, 303009, 'Firefighting and missed payments', true),
(30300902, 303009, 'Revenue recognition rules', false),
(30300903, 303009, 'Customer demand', false),
(30301001, 303010, 'Scenarios, assumptions, and rolling updates', true),
(30301002, 303010, 'Only last year profit', false),
(30301003, 303010, 'Only marketing budget', false)
on conflict (id) do nothing;

-- ========= Cost Control 101 (401): add 8 =========
insert into public.learning_quiz_questions (id, learning_module_id, question) values
(401003, 401, 'Variable costs change mainly with:'),
(401004, 401, 'Which is an example of a fixed cost?'),
(401005, 401, 'Cost control improves profit by:'),
(401006, 401, 'Which is an example of operational waste?'),
(401007, 401, 'A simple way to reduce waste is to:'),
(401008, 401, 'Why separate fixed vs variable costs?'),
(401009, 401, 'A good first cost-control action is to:'),
(401010, 401, 'Efficiency means:')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
(40100301, 401003, 'Sales volume / activity level', true),
(40100302, 401003, 'Brand color', false),
(40100303, 401003, 'Office address', false),
(40100401, 401004, 'Monthly rent', true),
(40100402, 401004, 'Raw materials', false),
(40100403, 401004, 'Packaging per order', false),
(40100501, 401005, 'Reducing unnecessary spending', true),
(40100502, 401005, 'Increasing refunds', false),
(40100503, 401005, 'Adding duplicate steps', false),
(40100601, 401006, 'Rework due to errors', true),
(40100602, 401006, 'Customer payment', false),
(40100603, 401006, 'Quality improvement', false),
(40100701, 401007, 'Standardize and remove non-value steps', true),
(40100702, 401007, 'Add more approvals', false),
(40100703, 401007, 'Delay decisions', false),
(40100801, 401008, 'To understand cost behavior and margins', true),
(40100802, 401008, 'To make costs disappear', false),
(40100803, 401008, 'To avoid accounting', false),
(40100901, 401009, 'Review top spend categories and renegotiate', true),
(40100902, 401009, 'Cut quality controls first', false),
(40100903, 401009, 'Stop paying suppliers', false),
(40101001, 401010, 'More output for the same input', true),
(40101002, 401010, 'More input for the same output', false),
(40101003, 401010, 'Ignoring process', false)
on conflict (id) do nothing;

-- ========= Operational Efficiency (402): add 8 =========
insert into public.learning_quiz_questions (id, learning_module_id, question) values
(402003, 402, 'Contribution margin is:'),
(402004, 402, 'Unit economics helps you decide:'),
(402005, 402, 'Automation is valuable mainly because it:'),
(402006, 402, 'Which task is best for automation?'),
(402007, 402, 'A strong unit economics metric is:'),
(402008, 402, 'If contribution margin is negative, you should:'),
(402009, 402, 'Process mapping is used to:'),
(402010, 402, 'A good ops KPI is:')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
(40200301, 402003, 'Revenue minus variable costs', true),
(40200302, 402003, 'Revenue minus fixed costs only', false),
(40200303, 402003, 'Profit after tax only', false),
(40200401, 402004, 'Which products/services to scale', true),
(40200402, 402004, 'Office decorations', false),
(40200403, 402004, 'Employee birthdays', false),
(40200501, 402005, 'Reduces time/cost on repeatable work', true),
(40200502, 402005, 'Guarantees more customers', false),
(40200503, 402005, 'Eliminates all mistakes forever', false),
(40200601, 402006, 'Sending standard reminders automatically', true),
(40200602, 402006, 'Complex one-off negotiations', false),
(40200603, 402006, 'Strategic rebranding', false),
(40200701, 402007, 'Contribution margin per unit', true),
(40200702, 402007, 'Total meetings per week', false),
(40200703, 402007, 'Number of emails sent', false),
(40200801, 402008, 'Adjust pricing/costs or stop that offering', true),
(40200802, 402008, 'Scale it immediately', false),
(40200803, 402008, 'Ignore the data', false),
(40200901, 402009, 'Visualize steps and identify bottlenecks', true),
(40200902, 402009, 'Increase complexity', false),
(40200903, 402009, 'Hide waste', false),
(40201001, 402010, 'Cycle time or cost per order', true),
(40201002, 402010, 'Number of trophies', false),
(40201003, 402010, 'Wallpaper quality', false)
on conflict (id) do nothing;

-- ========= Advanced Cost Optimization (403): add 8 =========
insert into public.learning_quiz_questions (id, learning_module_id, question) values
(403003, 403, 'Supplier renegotiation is MOST effective when you:'),
(403004, 403, 'Lean operations primarily targets:'),
(403005, 403, 'A common lean tool to reduce waste is:'),
(403006, 403, 'Consolidating spend helps because:'),
(403007, 403, 'Which is a lean �waste� example?'),
(403008, 403, 'The best supplier negotiation prep includes:'),
(403009, 403, 'Lean focuses on variability reduction to improve:'),
(403010, 403, 'A key risk of cost-cutting without strategy is:')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
(40300301, 403003, 'Use volume data and alternative quotes', true),
(40300302, 403003, 'Threaten suppliers immediately', false),
(40300303, 403003, 'Negotiate without data', false),
(40300401, 403004, 'Waste and variability', true),
(40300402, 403004, 'Brand awareness', false),
(40300403, 403004, 'Tax filing', false),
(40300501, 403005, 'Standard work / continuous improvement', true),
(40300502, 403005, 'Random workflow changes daily', false),
(40300503, 403005, 'Avoid measuring anything', false),
(40300601, 403006, 'It increases bargaining power', true),
(40300602, 403006, 'It guarantees zero defects', false),
(40300603, 403006, 'It removes contracts', false),
(40300701, 403007, 'Waiting time between steps', true),
(40300702, 403007, 'Customer value', false),
(40300703, 403007, 'Quality improvements', false),
(40300801, 403008, 'Know BATNA, volumes, and desired terms', true),
(40300802, 403008, 'Only rely on emotions', false),
(40300803, 403008, 'Avoid knowing current prices', false),
(40300901, 403009, 'Predictability and quality', true),
(40300902, 403009, 'Confusion and delays', false),
(40300903, 403009, 'Random outcomes', false),
(40301001, 403010, 'Reducing quality and causing churn', true),
(40301002, 403010, 'Increasing customer satisfaction always', false),
(40301003, 403010, 'Eliminating all future costs', false)
on conflict (id) do nothing;

-- ========= Pricing 101 (501): add 8 =========
insert into public.learning_quiz_questions (id, learning_module_id, question) values
(501003, 501, 'Cost-plus pricing means you price based on:'),
(501004, 501, 'Value-based pricing focuses mainly on:'),
(501005, 501, 'A discount should ideally be used to:'),
(501006, 501, 'Which is a risk of discounting too often?'),
(501007, 501, 'Revenue is calculated as:'),
(501008, 501, 'A good pricing habit is to:'),
(501009, 501, 'A pricing strategy should consider:'),
(501010, 501, 'A simple way to reduce pricing risk is:')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
(50100301, 501003, 'Cost + a markup', true),
(50100302, 501003, 'Customer mood only', false),
(50100303, 501003, 'Competitor logo design', false),
(50100401, 501004, 'Customer perceived value and outcomes', true),
(50100402, 501004, 'Only your internal costs', false),
(50100403, 501004, 'Only competitor price', false),
(50100501, 501005, 'Achieve a goal (trial, volume, retention)', true),
(50100502, 501005, 'Discount by default always', false),
(50100503, 501005, 'Hide real prices permanently', false),
(50100601, 501006, 'Customers expect discounts and margins fall', true),
(50100602, 501006, 'Guaranteed higher profit', false),
(50100603, 501006, 'Less customer churn always', false),
(50100701, 501007, 'Price � quantity sold', true),
(50100702, 501007, 'Profit � tax', false),
(50100703, 501007, 'Costs - revenue', false),
(50100801, 501008, 'Review prices periodically with data', true),
(50100802, 501008, 'Never change prices', false),
(50100803, 501008, 'Copy competitor blindly', false),
(50100901, 501009, 'Costs, customer value, and market context', true),
(50100902, 501009, 'Only personal preference', false),
(50100903, 501009, 'Only what feels nice', false),
(50101001, 501010, 'Offer tiers or bundles', true),
(50101002, 501010, 'Charge random amounts daily', false),
(50101003, 501010, 'Only one product forever', false)
on conflict (id) do nothing;

-- ========= Pricing Strategy (502): add 8 =========
insert into public.learning_quiz_questions (id, learning_module_id, question) values
(502003, 502, 'A price ladder is designed to:'),
(502004, 502, 'Discount discipline means you should:'),
(502005, 502, 'A good tiering strategy aligns tiers to:'),
(502006, 502, 'Which discount is generally safer?'),
(502007, 502, 'A common mistake in pricing is:'),
(502008, 502, 'To improve conversion without heavy discounting, you can:'),
(502009, 502, 'The best reason to offer a cheaper tier is:'),
(502010, 502, 'A pricing experiment should track:')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
(50200301, 502003, 'Match different willingness-to-pay segments', true),
(50200302, 502003, 'Make everything the same price', false),
(50200303, 502003, 'Hide all pricing', false),
(50200401, 502004, 'Discount with a clear objective and limit', true),
(50200402, 502004, 'Discount every time customers ask', false),
(50200403, 502004, 'Never track discount outcomes', false),
(50200501, 502005, 'Value/features/outcomes', true),
(50200502, 502005, 'Random features', false),
(50200503, 502005, 'Only internal politics', false),
(50200601, 502006, 'Time-limited, targeted promo', true),
(50200602, 502006, 'Permanent 50% off', false),
(50200603, 502006, 'Discounting to below cost', false),
(50200701, 502007, 'Pricing without understanding customer value', true),
(50200702, 502007, 'Measuring results', false),
(50200703, 502007, 'Improving packaging', false),
(50200801, 502008, 'Improve offer clarity and bundles', true),
(50200802, 502008, 'Remove all value propositions', false),
(50200803, 502008, 'Increase friction in checkout', false),
(50200901, 502009, 'Capture price-sensitive customers', true),
(50200902, 502009, 'Destroy brand trust', false),
(50200903, 502009, 'Guarantee everyone upgrades', false),
(50201001, 502010, 'Conversion, churn, and revenue per user', true),
(50201002, 502010, 'Logo clicks only', false),
(50201003, 502010, 'Number of screenshots taken', false)
on conflict (id) do nothing;

-- ========= Advanced Revenue Growth (503): add 8 =========
insert into public.learning_quiz_questions (id, learning_module_id, question) values
(503003, 503, 'Expansion revenue is increased mainly through:'),
(503004, 503, 'Retention economics improves because retained users:'),
(503005, 503, 'A good upsell strategy is to:'),
(503006, 503, 'Which metric is most linked to retention value?'),
(503007, 503, 'Reducing churn improves profitability because:'),
(503008, 503, 'A common retention lever is:'),
(503009, 503, 'Cross-sell works best when:'),
(503010, 503, 'The best advanced growth mix balances:')
on conflict (id) do nothing;

insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct) values
(50300301, 503003, 'Upsells and cross-sells to existing customers', true),
(50300302, 503003, 'Only lowering prices', false),
(50300303, 503003, 'Only spending more on ads', false),
(50300401, 503004, 'Cost less to serve than acquiring new users', true),
(50300402, 503004, 'Always demand refunds', false),
(50300403, 503004, 'Never use the product', false),
(50300501, 503005, 'Offer clear extra value at the right time', true),
(50300502, 503005, 'Force upgrades with no benefit', false),
(50300503, 503005, 'Hide features randomly', false),
(50300601, 503006, 'Net Revenue Retention (NRR)', true),
(50300602, 503006, 'Number of office plants', false),
(50300603, 503006, 'Clicks on homepage', false),
(50300701, 503007, 'Lifetime value increases while CAC stays fixed', true),
(50300702, 503007, 'Costs automatically go to zero', false),
(50300703, 503007, 'Taxes disappear', false),
(50300801, 503008, 'Improve onboarding and success outcomes', true),
(50300802, 503008, 'Increase hidden fees', false),
(50300803, 503008, 'Reduce support', false),
(50300901, 503009, 'It matches customer needs and context', true),
(50300902, 503009, 'It is unrelated to the product', false),
(50300903, 503009, 'It reduces perceived value', false),
(50301001, 503010, 'Acquisition, retention, and expansion', true),
(50301002, 503010, 'Only acquisition', false),
(50301003, 503010, 'Only discounts', false)
on conflict (id) do nothing;

-- ============================================================
-- IMPORTANT: Reset sequences (because we manually inserted IDs)
-- ============================================================
select setval(pg_get_serial_sequence('public.learning_topics','id'), (select coalesce(max(id),1) from public.learning_topics));
select setval(pg_get_serial_sequence('public.learning_modules','id'), (select coalesce(max(id),1) from public.learning_modules));
select setval(pg_get_serial_sequence('public.learning_sections','id'), (select coalesce(max(id),1) from public.learning_sections));
select setval(pg_get_serial_sequence('public.learning_quiz_questions','id'), (select coalesce(max(id),1) from public.learning_quiz_questions));
select setval(pg_get_serial_sequence('public.learning_quiz_options','id'), (select coalesce(max(id),1) from public.learning_quiz_options));
select setval(pg_get_serial_sequence('public.learning_progress','id'), (select coalesce(max(id),1) from public.learning_progress));

-- ===== END: learning_schema.sql =====

-- ===== BEGIN: platform_content_import.sql =====
-- ============================================================
-- Platform Content Import (from platform_content.xlsx)
-- Generated automatically
-- ============================================================

begin;

-- 1) Learning topics
insert into public.learning_topics (id, category, title, summary, estimated_minutes)
select 101, 'Platform', 'Technology Fundamentals', 'Imported from platform_content.xlsx', 30
where not exists (select 1 from public.learning_topics where id = 101);
insert into public.learning_topics (id, category, title, summary, estimated_minutes)
select 102, 'Platform', 'Data Science & Analytics', 'Imported from platform_content.xlsx', 30
where not exists (select 1 from public.learning_topics where id = 102);
insert into public.learning_topics (id, category, title, summary, estimated_minutes)
select 103, 'Platform', 'Digital Marketing', 'Imported from platform_content.xlsx', 30
where not exists (select 1 from public.learning_topics where id = 103);
insert into public.learning_topics (id, category, title, summary, estimated_minutes)
select 104, 'Platform', 'Product Management', 'Imported from platform_content.xlsx', 30
where not exists (select 1 from public.learning_topics where id = 104);
insert into public.learning_topics (id, category, title, summary, estimated_minutes)
select 105, 'Platform', 'Leadership & Strategy', 'Imported from platform_content.xlsx', 30
where not exists (select 1 from public.learning_topics where id = 105);

-- 2) Learning modules
insert into public.learning_modules (id, topic_id, difficulty, title)
select 2001, 101, 0, 'Introduction to Computing'
where not exists (select 1 from public.learning_modules where id = 2001);
insert into public.learning_modules (id, topic_id, difficulty, title)
select 2002, 101, 1, 'Software Development Basics'
where not exists (select 1 from public.learning_modules where id = 2002);
insert into public.learning_modules (id, topic_id, difficulty, title)
select 2003, 101, 2, 'System Architecture & Design'
where not exists (select 1 from public.learning_modules where id = 2003);
insert into public.learning_modules (id, topic_id, difficulty, title)
select 2004, 102, 0, 'Introduction to Data'
where not exists (select 1 from public.learning_modules where id = 2004);
insert into public.learning_modules (id, topic_id, difficulty, title)
select 2005, 102, 1, 'Statistical Analysis'
where not exists (select 1 from public.learning_modules where id = 2005);
insert into public.learning_modules (id, topic_id, difficulty, title)
select 2006, 102, 2, 'Machine Learning & AI'
where not exists (select 1 from public.learning_modules where id = 2006);
insert into public.learning_modules (id, topic_id, difficulty, title)
select 2007, 103, 0, 'Marketing Basics'
where not exists (select 1 from public.learning_modules where id = 2007);
insert into public.learning_modules (id, topic_id, difficulty, title)
select 2008, 103, 1, 'Social Media Strategy'
where not exists (select 1 from public.learning_modules where id = 2008);
insert into public.learning_modules (id, topic_id, difficulty, title)
select 2009, 103, 2, 'Analytics & Optimization'
where not exists (select 1 from public.learning_modules where id = 2009);
insert into public.learning_modules (id, topic_id, difficulty, title)
select 2010, 104, 0, 'Product Fundamentals'
where not exists (select 1 from public.learning_modules where id = 2010);
insert into public.learning_modules (id, topic_id, difficulty, title)
select 2011, 104, 1, 'Product Strategy'
where not exists (select 1 from public.learning_modules where id = 2011);
insert into public.learning_modules (id, topic_id, difficulty, title)
select 2012, 104, 2, 'Strategic Product Leadership'
where not exists (select 1 from public.learning_modules where id = 2012);
insert into public.learning_modules (id, topic_id, difficulty, title)
select 2013, 105, 0, 'Leadership Essentials'
where not exists (select 1 from public.learning_modules where id = 2013);
insert into public.learning_modules (id, topic_id, difficulty, title)
select 2014, 105, 1, 'Strategic Thinking'
where not exists (select 1 from public.learning_modules where id = 2014);
insert into public.learning_modules (id, topic_id, difficulty, title)
select 2015, 105, 2, 'Executive Leadership'
where not exists (select 1 from public.learning_modules where id = 2015);

-- 3) Learning sections (one intro section per imported module)
insert into public.learning_sections (id, module_id, "order", heading, body)
select 700000, 2001, 1, 'Introduction to Computing Overview', 'This section introduces key concepts for Introduction to Computing (Beginner) and prepares learners for the module quiz.'
where not exists (select 1 from public.learning_sections where id = 700000);
insert into public.learning_sections (id, module_id, "order", heading, body)
select 700001, 2002, 1, 'Software Development Basics Overview', 'This section introduces key concepts for Software Development Basics (Intermediate) and prepares learners for the module quiz.'
where not exists (select 1 from public.learning_sections where id = 700001);
insert into public.learning_sections (id, module_id, "order", heading, body)
select 700002, 2003, 1, 'System Architecture & Design Overview', 'This section introduces key concepts for System Architecture & Design (Advanced) and prepares learners for the module quiz.'
where not exists (select 1 from public.learning_sections where id = 700002);
insert into public.learning_sections (id, module_id, "order", heading, body)
select 700003, 2004, 1, 'Introduction to Data Overview', 'This section introduces key concepts for Introduction to Data (Beginner) and prepares learners for the module quiz.'
where not exists (select 1 from public.learning_sections where id = 700003);
insert into public.learning_sections (id, module_id, "order", heading, body)
select 700004, 2005, 1, 'Statistical Analysis Overview', 'This section introduces key concepts for Statistical Analysis (Intermediate) and prepares learners for the module quiz.'
where not exists (select 1 from public.learning_sections where id = 700004);
insert into public.learning_sections (id, module_id, "order", heading, body)
select 700005, 2006, 1, 'Machine Learning & AI Overview', 'This section introduces key concepts for Machine Learning & AI (Advanced) and prepares learners for the module quiz.'
where not exists (select 1 from public.learning_sections where id = 700005);
insert into public.learning_sections (id, module_id, "order", heading, body)
select 700006, 2007, 1, 'Marketing Basics Overview', 'This section introduces key concepts for Marketing Basics (Beginner) and prepares learners for the module quiz.'
where not exists (select 1 from public.learning_sections where id = 700006);
insert into public.learning_sections (id, module_id, "order", heading, body)
select 700007, 2008, 1, 'Social Media Strategy Overview', 'This section introduces key concepts for Social Media Strategy (Intermediate) and prepares learners for the module quiz.'
where not exists (select 1 from public.learning_sections where id = 700007);
insert into public.learning_sections (id, module_id, "order", heading, body)
select 700008, 2009, 1, 'Analytics & Optimization Overview', 'This section introduces key concepts for Analytics & Optimization (Advanced) and prepares learners for the module quiz.'
where not exists (select 1 from public.learning_sections where id = 700008);
insert into public.learning_sections (id, module_id, "order", heading, body)
select 700009, 2010, 1, 'Product Fundamentals Overview', 'This section introduces key concepts for Product Fundamentals (Beginner) and prepares learners for the module quiz.'
where not exists (select 1 from public.learning_sections where id = 700009);
insert into public.learning_sections (id, module_id, "order", heading, body)
select 700010, 2011, 1, 'Product Strategy Overview', 'This section introduces key concepts for Product Strategy (Intermediate) and prepares learners for the module quiz.'
where not exists (select 1 from public.learning_sections where id = 700010);
insert into public.learning_sections (id, module_id, "order", heading, body)
select 700011, 2012, 1, 'Strategic Product Leadership Overview', 'This section introduces key concepts for Strategic Product Leadership (Advanced) and prepares learners for the module quiz.'
where not exists (select 1 from public.learning_sections where id = 700011);
insert into public.learning_sections (id, module_id, "order", heading, body)
select 700012, 2013, 1, 'Leadership Essentials Overview', 'This section introduces key concepts for Leadership Essentials (Beginner) and prepares learners for the module quiz.'
where not exists (select 1 from public.learning_sections where id = 700012);
insert into public.learning_sections (id, module_id, "order", heading, body)
select 700013, 2014, 1, 'Strategic Thinking Overview', 'This section introduces key concepts for Strategic Thinking (Intermediate) and prepares learners for the module quiz.'
where not exists (select 1 from public.learning_sections where id = 700013);
insert into public.learning_sections (id, module_id, "order", heading, body)
select 700014, 2015, 1, 'Executive Leadership Overview', 'This section introduces key concepts for Executive Leadership (Advanced) and prepares learners for the module quiz.'
where not exists (select 1 from public.learning_sections where id = 700014);

-- 4) Quiz questions and options (4 options each)
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800000, 2001, 'What does CPU stand for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800000);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900000, 800000, 'Central Processing Unit', true
where not exists (select 1 from public.learning_quiz_options where id = 900000);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900001, 800000, 'Computer Personal Unit', false
where not exists (select 1 from public.learning_quiz_options where id = 900001);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900002, 800000, 'Central Program Utility', false
where not exists (select 1 from public.learning_quiz_options where id = 900002);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900003, 800000, 'Computer Processing Utility', false
where not exists (select 1 from public.learning_quiz_options where id = 900003);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800001, 2001, 'Which of the following is an example of volatile memory'''
where not exists (select 1 from public.learning_quiz_questions where id = 800001);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900004, 800001, 'Hard Drive', false
where not exists (select 1 from public.learning_quiz_options where id = 900004);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900005, 800001, 'SSD', false
where not exists (select 1 from public.learning_quiz_options where id = 900005);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900006, 800001, 'RAM', true
where not exists (select 1 from public.learning_quiz_options where id = 900006);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900007, 800001, 'USB Drive', false
where not exists (select 1 from public.learning_quiz_options where id = 900007);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800002, 2001, 'What is the primary function of an operating system'''
where not exists (select 1 from public.learning_quiz_questions where id = 800002);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900008, 800002, 'Browse the internet', false
where not exists (select 1 from public.learning_quiz_options where id = 900008);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900009, 800002, 'Manage computer hardware and software resources', true
where not exists (select 1 from public.learning_quiz_options where id = 900009);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900010, 800002, 'Create documents', false
where not exists (select 1 from public.learning_quiz_options where id = 900010);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900011, 800002, 'Play games', false
where not exists (select 1 from public.learning_quiz_options where id = 900011);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800003, 2001, 'Which protocol is used for sending emails'''
where not exists (select 1 from public.learning_quiz_questions where id = 800003);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900012, 800003, 'HTTP', false
where not exists (select 1 from public.learning_quiz_options where id = 900012);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900013, 800003, 'FTP', false
where not exists (select 1 from public.learning_quiz_options where id = 900013);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900014, 800003, 'SMTP', true
where not exists (select 1 from public.learning_quiz_options where id = 900014);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900015, 800003, 'DNS', false
where not exists (select 1 from public.learning_quiz_options where id = 900015);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800004, 2001, 'What does HTML stand for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800004);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900016, 800004, 'Hyper Text Markup Language', true
where not exists (select 1 from public.learning_quiz_options where id = 900016);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900017, 800004, 'High Tech Modern Language', false
where not exists (select 1 from public.learning_quiz_options where id = 900017);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900018, 800004, 'Home Tool Markup Language', false
where not exists (select 1 from public.learning_quiz_options where id = 900018);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900019, 800004, 'Hyperlinks and Text Markup Language', false
where not exists (select 1 from public.learning_quiz_options where id = 900019);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800005, 2001, 'Which storage device has the fastest data access speed'''
where not exists (select 1 from public.learning_quiz_questions where id = 800005);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900020, 800005, 'HDD', false
where not exists (select 1 from public.learning_quiz_options where id = 900020);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900021, 800005, 'CD-ROM', false
where not exists (select 1 from public.learning_quiz_options where id = 900021);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900022, 800005, 'SSD', true
where not exists (select 1 from public.learning_quiz_options where id = 900022);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900023, 800005, 'Floppy Disk', false
where not exists (select 1 from public.learning_quiz_options where id = 900023);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800006, 2001, 'What is a firewall used for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800006);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900024, 800006, 'Storing files', false
where not exists (select 1 from public.learning_quiz_options where id = 900024);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900025, 800006, 'Network security', true
where not exists (select 1 from public.learning_quiz_options where id = 900025);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900026, 800006, 'Data compression', false
where not exists (select 1 from public.learning_quiz_options where id = 900026);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900027, 800006, 'Email management', false
where not exists (select 1 from public.learning_quiz_options where id = 900027);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800007, 2001, 'Which language is primarily used for web page styling'''
where not exists (select 1 from public.learning_quiz_questions where id = 800007);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900028, 800007, 'Python', false
where not exists (select 1 from public.learning_quiz_options where id = 900028);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900029, 800007, 'CSS', true
where not exists (select 1 from public.learning_quiz_options where id = 900029);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900030, 800007, 'Java', false
where not exists (select 1 from public.learning_quiz_options where id = 900030);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900031, 800007, 'C++', false
where not exists (select 1 from public.learning_quiz_options where id = 900031);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800008, 2001, 'What does URL stand for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800008);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900032, 800008, 'Universal Resource Locator', false
where not exists (select 1 from public.learning_quiz_options where id = 900032);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900033, 800008, 'Uniform Resource Locator', true
where not exists (select 1 from public.learning_quiz_options where id = 900033);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900034, 800008, 'Universal Reference Link', false
where not exists (select 1 from public.learning_quiz_options where id = 900034);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900035, 800008, 'Uniform Reference Locator', false
where not exists (select 1 from public.learning_quiz_options where id = 900035);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800009, 2001, 'Which of the following is NOT an input device'''
where not exists (select 1 from public.learning_quiz_questions where id = 800009);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900036, 800009, 'Keyboard', false
where not exists (select 1 from public.learning_quiz_options where id = 900036);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900037, 800009, 'Mouse', false
where not exists (select 1 from public.learning_quiz_options where id = 900037);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900038, 800009, 'Monitor', true
where not exists (select 1 from public.learning_quiz_options where id = 900038);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900039, 800009, 'Scanner', false
where not exists (select 1 from public.learning_quiz_options where id = 900039);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800010, 2002, 'What is the main purpose of version control systems like Git'''
where not exists (select 1 from public.learning_quiz_questions where id = 800010);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900040, 800010, 'To compile code', false
where not exists (select 1 from public.learning_quiz_options where id = 900040);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900041, 800010, 'To track changes in code over time', true
where not exists (select 1 from public.learning_quiz_options where id = 900041);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900042, 800010, 'To debug programs', false
where not exists (select 1 from public.learning_quiz_options where id = 900042);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900043, 800010, 'To encrypt files', false
where not exists (select 1 from public.learning_quiz_options where id = 900043);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800011, 2002, 'Which design pattern ensures a class has only one instance'''
where not exists (select 1 from public.learning_quiz_questions where id = 800011);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900044, 800011, 'Factory', false
where not exists (select 1 from public.learning_quiz_options where id = 900044);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900045, 800011, 'Singleton', true
where not exists (select 1 from public.learning_quiz_options where id = 900045);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900046, 800011, 'Observer', false
where not exists (select 1 from public.learning_quiz_options where id = 900046);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900047, 800011, 'Decorator', false
where not exists (select 1 from public.learning_quiz_options where id = 900047);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800012, 2002, 'What does API stand for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800012);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900048, 800012, 'Application Programming Interface', true
where not exists (select 1 from public.learning_quiz_options where id = 900048);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900049, 800012, 'Advanced Programming Integration', false
where not exists (select 1 from public.learning_quiz_options where id = 900049);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900050, 800012, 'Automated Program Instruction', false
where not exists (select 1 from public.learning_quiz_options where id = 900050);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900051, 800012, 'Application Process Interface', false
where not exists (select 1 from public.learning_quiz_options where id = 900051);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800013, 2002, 'In object-oriented programming, what is encapsulation'''
where not exists (select 1 from public.learning_quiz_questions where id = 800013);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900052, 800013, 'Running multiple processes simultaneously', false
where not exists (select 1 from public.learning_quiz_options where id = 900052);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900053, 800013, 'Bundling data and methods that work on that data', true
where not exists (select 1 from public.learning_quiz_options where id = 900053);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900054, 800013, 'Converting code to machine language', false
where not exists (select 1 from public.learning_quiz_options where id = 900054);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900055, 800013, 'Optimizing code performance', false
where not exists (select 1 from public.learning_quiz_options where id = 900055);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800014, 2002, 'What is the time complexity of binary search'''
where not exists (select 1 from public.learning_quiz_questions where id = 800014);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900056, 800014, 'O(n)', false
where not exists (select 1 from public.learning_quiz_options where id = 900056);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900057, 800014, 'O(log n)', true
where not exists (select 1 from public.learning_quiz_options where id = 900057);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900058, 800014, 'O(n²)', false
where not exists (select 1 from public.learning_quiz_options where id = 900058);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900059, 800014, 'O(1)', false
where not exists (select 1 from public.learning_quiz_options where id = 900059);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800015, 2002, 'Which HTTP method is used to update existing resources'''
where not exists (select 1 from public.learning_quiz_questions where id = 800015);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900060, 800015, 'GET', false
where not exists (select 1 from public.learning_quiz_options where id = 900060);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900061, 800015, 'POST', false
where not exists (select 1 from public.learning_quiz_options where id = 900061);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900062, 800015, 'PUT', true
where not exists (select 1 from public.learning_quiz_options where id = 900062);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900063, 800015, 'DELETE', false
where not exists (select 1 from public.learning_quiz_options where id = 900063);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800016, 2002, 'What is a race condition'''
where not exists (select 1 from public.learning_quiz_questions where id = 800016);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900064, 800016, 'A competition between programmers', false
where not exists (select 1 from public.learning_quiz_options where id = 900064);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900065, 800016, 'When multiple threads access shared data simultaneously', true
where not exists (select 1 from public.learning_quiz_options where id = 900065);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900066, 800016, 'A type of loop', false
where not exists (select 1 from public.learning_quiz_options where id = 900066);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900067, 800016, 'An error in syntax', false
where not exists (select 1 from public.learning_quiz_options where id = 900067);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800017, 2002, 'What does SQL stand for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800017);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900068, 800017, 'Structured Query Language', true
where not exists (select 1 from public.learning_quiz_options where id = 900068);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900069, 800017, 'Simple Question Language', false
where not exists (select 1 from public.learning_quiz_options where id = 900069);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900070, 800017, 'System Quality Logic', false
where not exists (select 1 from public.learning_quiz_options where id = 900070);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900071, 800017, 'Standard Query List', false
where not exists (select 1 from public.learning_quiz_options where id = 900071);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800018, 2002, 'What is the purpose of a unit test'''
where not exists (select 1 from public.learning_quiz_questions where id = 800018);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900072, 800018, 'Test the entire application', false
where not exists (select 1 from public.learning_quiz_options where id = 900072);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900073, 800018, 'Test individual components in isolation', true
where not exists (select 1 from public.learning_quiz_options where id = 900073);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900074, 800018, 'Test user interface', false
where not exists (select 1 from public.learning_quiz_options where id = 900074);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900075, 800018, 'Test network connectivity', false
where not exists (select 1 from public.learning_quiz_options where id = 900075);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800019, 2002, 'Which data structure uses LIFO (Last In, First Out)'''
where not exists (select 1 from public.learning_quiz_questions where id = 800019);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900076, 800019, 'Queue', false
where not exists (select 1 from public.learning_quiz_options where id = 900076);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900077, 800019, 'Stack', true
where not exists (select 1 from public.learning_quiz_options where id = 900077);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900078, 800019, 'Array', false
where not exists (select 1 from public.learning_quiz_options where id = 900078);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900079, 800019, 'Linked List', false
where not exists (select 1 from public.learning_quiz_options where id = 900079);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800020, 2003, 'What is the CAP theorem in distributed systems'''
where not exists (select 1 from public.learning_quiz_questions where id = 800020);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900080, 800020, 'A theorem about network speeds', false
where not exists (select 1 from public.learning_quiz_options where id = 900080);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900081, 800020, 'States you can only guarantee 2 of 3: Consistency, Availability, Partition tolerance', true
where not exists (select 1 from public.learning_quiz_options where id = 900081);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900082, 800020, 'A security principle', false
where not exists (select 1 from public.learning_quiz_options where id = 900082);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900083, 800020, 'A database indexing method', false
where not exists (select 1 from public.learning_quiz_options where id = 900083);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800021, 2003, 'What is eventual consistency'''
where not exists (select 1 from public.learning_quiz_questions where id = 800021);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900084, 800021, 'Data is always consistent', false
where not exists (select 1 from public.learning_quiz_options where id = 900084);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900085, 800021, 'Data becomes consistent after a delay', true
where not exists (select 1 from public.learning_quiz_options where id = 900085);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900086, 800021, 'Data is never consistent', false
where not exists (select 1 from public.learning_quiz_options where id = 900086);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900087, 800021, 'Consistency is not guaranteed', false
where not exists (select 1 from public.learning_quiz_options where id = 900087);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800022, 2003, 'Which architectural pattern separates reading and writing operations'''
where not exists (select 1 from public.learning_quiz_questions where id = 800022);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900088, 800022, 'MVC', false
where not exists (select 1 from public.learning_quiz_options where id = 900088);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900089, 800022, 'CQRS', true
where not exists (select 1 from public.learning_quiz_options where id = 900089);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900090, 800022, 'Microservices', false
where not exists (select 1 from public.learning_quiz_options where id = 900090);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900091, 800022, 'Layered Architecture', false
where not exists (select 1 from public.learning_quiz_options where id = 900091);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800023, 2003, 'What is the primary benefit of containerization with Docker'''
where not exists (select 1 from public.learning_quiz_questions where id = 800023);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900092, 800023, 'Faster code execution', false
where not exists (select 1 from public.learning_quiz_options where id = 900092);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900093, 800023, 'Consistent environment across different systems', true
where not exists (select 1 from public.learning_quiz_options where id = 900093);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900094, 800023, 'Automatic bug fixing', false
where not exists (select 1 from public.learning_quiz_options where id = 900094);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900095, 800023, 'Better graphics rendering', false
where not exists (select 1 from public.learning_quiz_options where id = 900095);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800024, 2003, 'In microservices architecture, what is service discovery'''
where not exists (select 1 from public.learning_quiz_questions where id = 800024);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900096, 800024, 'Finding bugs in services', false
where not exists (select 1 from public.learning_quiz_options where id = 900096);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900097, 800024, 'Locating network addresses of service instances', true
where not exists (select 1 from public.learning_quiz_options where id = 900097);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900098, 800024, 'Monitoring service performance', false
where not exists (select 1 from public.learning_quiz_options where id = 900098);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900099, 800024, 'Documenting services', false
where not exists (select 1 from public.learning_quiz_options where id = 900099);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800025, 2003, 'What is a message queue used for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800025);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900100, 800025, 'Storing user messages', false
where not exists (select 1 from public.learning_quiz_options where id = 900100);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900101, 800025, 'Asynchronous communication between services', true
where not exists (select 1 from public.learning_quiz_options where id = 900101);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900102, 800025, 'Email delivery', false
where not exists (select 1 from public.learning_quiz_options where id = 900102);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900103, 800025, 'File storage', false
where not exists (select 1 from public.learning_quiz_options where id = 900103);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800026, 2003, 'What is the purpose of load balancing'''
where not exists (select 1 from public.learning_quiz_questions where id = 800026);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900104, 800026, 'Reduce server costs', false
where not exists (select 1 from public.learning_quiz_options where id = 900104);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900105, 800026, 'Distribute traffic across multiple servers', true
where not exists (select 1 from public.learning_quiz_options where id = 900105);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900106, 800026, 'Increase storage capacity', false
where not exists (select 1 from public.learning_quiz_options where id = 900106);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900107, 800026, 'Improve database queries', false
where not exists (select 1 from public.learning_quiz_options where id = 900107);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800027, 2003, 'What is idempotency in API design'''
where not exists (select 1 from public.learning_quiz_questions where id = 800027);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900108, 800027, 'The ability to process requests in parallel', false
where not exists (select 1 from public.learning_quiz_options where id = 900108);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900109, 800027, 'Making the same request multiple times has the same effect as making it once', true
where not exists (select 1 from public.learning_quiz_options where id = 900109);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900110, 800027, 'Encrypting API responses', false
where not exists (select 1 from public.learning_quiz_options where id = 900110);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900111, 800027, 'Versioning APIs', false
where not exists (select 1 from public.learning_quiz_options where id = 900111);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800028, 2003, 'What is the main advantage of GraphQL over REST'''
where not exists (select 1 from public.learning_quiz_questions where id = 800028);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900112, 800028, 'Better security', false
where not exists (select 1 from public.learning_quiz_options where id = 900112);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900113, 800028, 'Clients can request exactly the data they need', true
where not exists (select 1 from public.learning_quiz_options where id = 900113);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900114, 800028, 'Faster processing', false
where not exists (select 1 from public.learning_quiz_options where id = 900114);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900115, 800028, 'Simpler implementation', false
where not exists (select 1 from public.learning_quiz_options where id = 900115);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800029, 2003, 'What is sharding in database systems'''
where not exists (select 1 from public.learning_quiz_questions where id = 800029);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900116, 800029, 'Backing up data', false
where not exists (select 1 from public.learning_quiz_options where id = 900116);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900117, 800029, 'Horizontally partitioning data across multiple databases', true
where not exists (select 1 from public.learning_quiz_options where id = 900117);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900118, 800029, 'Encrypting database contents', false
where not exists (select 1 from public.learning_quiz_options where id = 900118);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900119, 800029, 'Compressing data', false
where not exists (select 1 from public.learning_quiz_options where id = 900119);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800030, 2004, 'What is the median of the dataset: 3, 7, 9, 15, 20'''
where not exists (select 1 from public.learning_quiz_questions where id = 800030);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900120, 800030, '7', false
where not exists (select 1 from public.learning_quiz_options where id = 900120);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900121, 800030, '9', true
where not exists (select 1 from public.learning_quiz_options where id = 900121);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900122, 800030, '10', false
where not exists (select 1 from public.learning_quiz_options where id = 900122);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900123, 800030, '15', false
where not exists (select 1 from public.learning_quiz_options where id = 900123);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800031, 2004, 'Which measure of central tendency is most affected by outliers'''
where not exists (select 1 from public.learning_quiz_questions where id = 800031);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900124, 800031, 'Mean', true
where not exists (select 1 from public.learning_quiz_options where id = 900124);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900125, 800031, 'Median', false
where not exists (select 1 from public.learning_quiz_options where id = 900125);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900126, 800031, 'Mode', false
where not exists (select 1 from public.learning_quiz_options where id = 900126);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900127, 800031, 'Range', false
where not exists (select 1 from public.learning_quiz_options where id = 900127);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800032, 2004, 'What type of data is "customer satisfaction rating (1-5)"'''
where not exists (select 1 from public.learning_quiz_questions where id = 800032);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900128, 800032, 'Nominal', false
where not exists (select 1 from public.learning_quiz_options where id = 900128);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900129, 800032, 'Ordinal', true
where not exists (select 1 from public.learning_quiz_options where id = 900129);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900130, 800032, 'Interval', false
where not exists (select 1 from public.learning_quiz_options where id = 900130);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900131, 800032, 'Ratio', false
where not exists (select 1 from public.learning_quiz_options where id = 900131);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800033, 2004, 'What does CSV stand for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800033);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900132, 800033, 'Computer Separated Values', false
where not exists (select 1 from public.learning_quiz_options where id = 900132);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900133, 800033, 'Comma Separated Values', true
where not exists (select 1 from public.learning_quiz_options where id = 900133);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900134, 800033, 'Central System Values', false
where not exists (select 1 from public.learning_quiz_options where id = 900134);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900135, 800033, 'Column Separated Variables', false
where not exists (select 1 from public.learning_quiz_options where id = 900135);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800034, 2004, 'Which chart is best for showing parts of a whole'''
where not exists (select 1 from public.learning_quiz_questions where id = 800034);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900136, 800034, 'Line chart', false
where not exists (select 1 from public.learning_quiz_options where id = 900136);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900137, 800034, 'Scatter plot', false
where not exists (select 1 from public.learning_quiz_options where id = 900137);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900138, 800034, 'Pie chart', true
where not exists (select 1 from public.learning_quiz_options where id = 900138);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900139, 800034, 'Bar chart', false
where not exists (select 1 from public.learning_quiz_options where id = 900139);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800035, 2004, 'What is a variable that can only take specific, distinct values called'''
where not exists (select 1 from public.learning_quiz_questions where id = 800035);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900140, 800035, 'Continuous', false
where not exists (select 1 from public.learning_quiz_options where id = 900140);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900141, 800035, 'Discrete', true
where not exists (select 1 from public.learning_quiz_options where id = 900141);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900142, 800035, 'Dependent', false
where not exists (select 1 from public.learning_quiz_options where id = 900142);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900143, 800035, 'Independent', false
where not exists (select 1 from public.learning_quiz_options where id = 900143);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800036, 2004, 'In a dataset, what is a row typically called'''
where not exists (select 1 from public.learning_quiz_questions where id = 800036);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900144, 800036, 'Variable', false
where not exists (select 1 from public.learning_quiz_options where id = 900144);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900145, 800036, 'Field', false
where not exists (select 1 from public.learning_quiz_options where id = 900145);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900146, 800036, 'Record', true
where not exists (select 1 from public.learning_quiz_options where id = 900146);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900147, 800036, 'Column', false
where not exists (select 1 from public.learning_quiz_options where id = 900147);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800037, 2004, 'What does the term "data cleaning" refer to'''
where not exists (select 1 from public.learning_quiz_questions where id = 800037);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900148, 800037, 'Deleting all data', false
where not exists (select 1 from public.learning_quiz_options where id = 900148);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900149, 800037, 'Removing errors and inconsistencies from data', true
where not exists (select 1 from public.learning_quiz_options where id = 900149);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900150, 800037, 'Organizing files', false
where not exists (select 1 from public.learning_quiz_options where id = 900150);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900151, 800037, 'Encrypting data', false
where not exists (select 1 from public.learning_quiz_options where id = 900151);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800038, 2004, 'Which of the following is an example of qualitative data'''
where not exists (select 1 from public.learning_quiz_questions where id = 800038);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900152, 800038, 'Temperature', false
where not exists (select 1 from public.learning_quiz_options where id = 900152);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900153, 800038, 'Customer feedback comments', true
where not exists (select 1 from public.learning_quiz_options where id = 900153);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900154, 800038, 'Sales revenue', false
where not exists (select 1 from public.learning_quiz_options where id = 900154);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900155, 800038, 'Age', false
where not exists (select 1 from public.learning_quiz_options where id = 900155);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800039, 2004, 'What is the range of a dataset'''
where not exists (select 1 from public.learning_quiz_questions where id = 800039);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900156, 800039, 'The most frequent value', false
where not exists (select 1 from public.learning_quiz_options where id = 900156);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900157, 800039, 'The middle value', false
where not exists (select 1 from public.learning_quiz_options where id = 900157);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900158, 800039, 'The difference between maximum and minimum values', true
where not exists (select 1 from public.learning_quiz_options where id = 900158);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900159, 800039, 'The average value', false
where not exists (select 1 from public.learning_quiz_options where id = 900159);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800040, 2005, 'What is the p-value in hypothesis testing'''
where not exists (select 1 from public.learning_quiz_questions where id = 800040);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900160, 800040, 'The probability of the null hypothesis being true', false
where not exists (select 1 from public.learning_quiz_options where id = 900160);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900161, 800040, 'The probability of observing results at least as extreme as those observed, assuming the null hypothesis is true', true
where not exists (select 1 from public.learning_quiz_options where id = 900161);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900162, 800040, 'The confidence level', false
where not exists (select 1 from public.learning_quiz_options where id = 900162);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900163, 800040, 'The sample size', false
where not exists (select 1 from public.learning_quiz_options where id = 900163);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800041, 2005, 'What does a correlation coefficient of -0.9 indicate'''
where not exists (select 1 from public.learning_quiz_questions where id = 800041);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900164, 800041, 'No relationship', false
where not exists (select 1 from public.learning_quiz_options where id = 900164);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900165, 800041, 'Weak positive relationship', false
where not exists (select 1 from public.learning_quiz_options where id = 900165);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900166, 800041, 'Strong negative relationship', true
where not exists (select 1 from public.learning_quiz_options where id = 900166);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900167, 800041, 'Weak negative relationship', false
where not exists (select 1 from public.learning_quiz_options where id = 900167);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800042, 2005, 'What is the purpose of A/B testing'''
where not exists (select 1 from public.learning_quiz_questions where id = 800042);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900168, 800042, 'To test database performance', false
where not exists (select 1 from public.learning_quiz_options where id = 900168);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900169, 800042, 'To compare two versions to determine which performs better', true
where not exists (select 1 from public.learning_quiz_options where id = 900169);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900170, 800042, 'To backup data', false
where not exists (select 1 from public.learning_quiz_options where id = 900170);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900171, 800042, 'To encrypt information', false
where not exists (select 1 from public.learning_quiz_options where id = 900171);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800043, 2005, 'What is overfitting in machine learning'''
where not exists (select 1 from public.learning_quiz_questions where id = 800043);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900172, 800043, 'Model performs well on training data but poorly on new data', true
where not exists (select 1 from public.learning_quiz_options where id = 900172);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900173, 800043, 'Model performs poorly on all data', false
where not exists (select 1 from public.learning_quiz_options where id = 900173);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900174, 800043, 'Model takes too long to train', false
where not exists (select 1 from public.learning_quiz_options where id = 900174);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900175, 800043, 'Model is too simple', false
where not exists (select 1 from public.learning_quiz_options where id = 900175);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800044, 2005, 'Which sampling method ensures every member of the population has an equal chance of being selected'''
where not exists (select 1 from public.learning_quiz_questions where id = 800044);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900176, 800044, 'Convenience sampling', false
where not exists (select 1 from public.learning_quiz_options where id = 900176);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900177, 800044, 'Random sampling', true
where not exists (select 1 from public.learning_quiz_options where id = 900177);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900178, 800044, 'Stratified sampling', false
where not exists (select 1 from public.learning_quiz_options where id = 900178);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900179, 800044, 'Cluster sampling', false
where not exists (select 1 from public.learning_quiz_options where id = 900179);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800045, 2005, 'What is the Central Limit Theorem'''
where not exists (select 1 from public.learning_quiz_questions where id = 800045);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900180, 800045, 'Large datasets always follow normal distribution', false
where not exists (select 1 from public.learning_quiz_options where id = 900180);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900181, 800045, 'Sample means approximate normal distribution as sample size increases', true
where not exists (select 1 from public.learning_quiz_options where id = 900181);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900182, 800045, 'All data is centered around the mean', false
where not exists (select 1 from public.learning_quiz_options where id = 900182);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900183, 800045, 'Outliers should be removed', false
where not exists (select 1 from public.learning_quiz_options where id = 900183);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800046, 2005, 'What is a Type I error'''
where not exists (select 1 from public.learning_quiz_questions where id = 800046);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900184, 800046, 'Rejecting a true null hypothesis', true
where not exists (select 1 from public.learning_quiz_options where id = 900184);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900185, 800046, 'Accepting a false null hypothesis', false
where not exists (select 1 from public.learning_quiz_options where id = 900185);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900186, 800046, 'Using wrong sample size', false
where not exists (select 1 from public.learning_quiz_options where id = 900186);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900187, 800046, 'Measurement error', false
where not exists (select 1 from public.learning_quiz_options where id = 900187);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800047, 2005, 'What does R² (R-squared) measure in regression'''
where not exists (select 1 from public.learning_quiz_questions where id = 800047);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900188, 800047, 'The average error', false
where not exists (select 1 from public.learning_quiz_options where id = 900188);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900189, 800047, 'The proportion of variance explained by the model', true
where not exists (select 1 from public.learning_quiz_options where id = 900189);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900190, 800047, 'The correlation coefficient', false
where not exists (select 1 from public.learning_quiz_options where id = 900190);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900191, 800047, 'The sample size needed', false
where not exists (select 1 from public.learning_quiz_options where id = 900191);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800048, 2005, 'What is the purpose of feature scaling'''
where not exists (select 1 from public.learning_quiz_questions where id = 800048);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900192, 800048, 'To reduce the number of features', false
where not exists (select 1 from public.learning_quiz_options where id = 900192);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900193, 800048, 'To normalize features to a similar range', true
where not exists (select 1 from public.learning_quiz_options where id = 900193);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900194, 800048, 'To create new features', false
where not exists (select 1 from public.learning_quiz_options where id = 900194);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900195, 800048, 'To remove outliers', false
where not exists (select 1 from public.learning_quiz_options where id = 900195);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800049, 2005, 'What is cross-validation used for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800049);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900196, 800049, 'Checking data entry accuracy', false
where not exists (select 1 from public.learning_quiz_options where id = 900196);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900197, 800049, 'Assessing model performance and avoiding overfitting', true
where not exists (select 1 from public.learning_quiz_options where id = 900197);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900198, 800049, 'Encrypting data', false
where not exists (select 1 from public.learning_quiz_options where id = 900198);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900199, 800049, 'Cleaning data', false
where not exists (select 1 from public.learning_quiz_options where id = 900199);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800050, 2006, 'What is the vanishing gradient problem in deep learning'''
where not exists (select 1 from public.learning_quiz_questions where id = 800050);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900200, 800050, 'Gradients become too large', false
where not exists (select 1 from public.learning_quiz_options where id = 900200);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900201, 800050, 'Gradients become too small to update weights effectively', true
where not exists (select 1 from public.learning_quiz_options where id = 900201);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900202, 800050, 'Model stops learning', false
where not exists (select 1 from public.learning_quiz_options where id = 900202);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900203, 800050, 'Data becomes corrupted', false
where not exists (select 1 from public.learning_quiz_options where id = 900203);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800051, 2006, 'Which algorithm is best for dimensionality reduction while preserving variance'''
where not exists (select 1 from public.learning_quiz_questions where id = 800051);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900204, 800051, 'K-means', false
where not exists (select 1 from public.learning_quiz_options where id = 900204);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900205, 800051, 'PCA', true
where not exists (select 1 from public.learning_quiz_options where id = 900205);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900206, 800051, 'Decision Trees', false
where not exists (select 1 from public.learning_quiz_options where id = 900206);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900207, 800051, 'Linear Regression', false
where not exists (select 1 from public.learning_quiz_options where id = 900207);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800052, 2006, 'What is the purpose of batch normalization'''
where not exists (select 1 from public.learning_quiz_questions where id = 800052);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900208, 800052, 'Process data in batches', false
where not exists (select 1 from public.learning_quiz_options where id = 900208);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900209, 800052, 'Normalize layer inputs to stabilize training', true
where not exists (select 1 from public.learning_quiz_options where id = 900209);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900210, 800052, 'Reduce batch size', false
where not exists (select 1 from public.learning_quiz_options where id = 900210);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900211, 800052, 'Increase training speed', false
where not exists (select 1 from public.learning_quiz_options where id = 900211);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800053, 2006, 'What is an ensemble method in machine learning'''
where not exists (select 1 from public.learning_quiz_questions where id = 800053);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900212, 800053, 'Using a single powerful model', false
where not exists (select 1 from public.learning_quiz_options where id = 900212);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900213, 800053, 'Combining predictions from multiple models', true
where not exists (select 1 from public.learning_quiz_options where id = 900213);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900214, 800053, 'Training on the entire dataset', false
where not exists (select 1 from public.learning_quiz_options where id = 900214);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900215, 800053, 'Using deep neural networks', false
where not exists (select 1 from public.learning_quiz_options where id = 900215);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800054, 2006, 'What is transfer learning'''
where not exists (select 1 from public.learning_quiz_questions where id = 800054);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900216, 800054, 'Transferring data between systems', false
where not exists (select 1 from public.learning_quiz_options where id = 900216);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900217, 800054, 'Using pre-trained models on new related tasks', true
where not exists (select 1 from public.learning_quiz_options where id = 900217);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900218, 800054, 'Converting models to different formats', false
where not exists (select 1 from public.learning_quiz_options where id = 900218);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900219, 800054, 'Sharing models online', false
where not exists (select 1 from public.learning_quiz_options where id = 900219);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800055, 2006, 'What is the attention mechanism in neural networks'''
where not exists (select 1 from public.learning_quiz_questions where id = 800055);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900220, 800055, 'A way to focus computational resources', false
where not exists (select 1 from public.learning_quiz_options where id = 900220);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900221, 800055, 'A mechanism allowing models to focus on relevant parts of input', true
where not exists (select 1 from public.learning_quiz_options where id = 900221);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900222, 800055, 'A regularization technique', false
where not exists (select 1 from public.learning_quiz_options where id = 900222);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900223, 800055, 'A type of activation function', false
where not exists (select 1 from public.learning_quiz_options where id = 900223);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800056, 2006, 'What is the difference between generative and discriminative models'''
where not exists (select 1 from public.learning_quiz_questions where id = 800056);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900224, 800056, 'Generative models create new data, discriminative models classify existing data', false
where not exists (select 1 from public.learning_quiz_options where id = 900224);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900225, 800056, 'Generative models model P(X|Y), discriminative models model P(Y|X)', true
where not exists (select 1 from public.learning_quiz_options where id = 900225);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900226, 800056, 'No difference', false
where not exists (select 1 from public.learning_quiz_options where id = 900226);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900227, 800056, 'Discriminative models are always better', false
where not exists (select 1 from public.learning_quiz_options where id = 900227);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800057, 2006, 'What is reinforcement learning'''
where not exists (select 1 from public.learning_quiz_questions where id = 800057);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900228, 800057, 'Learning from labeled data', false
where not exists (select 1 from public.learning_quiz_options where id = 900228);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900229, 800057, 'Learning through interaction and rewards', true
where not exists (select 1 from public.learning_quiz_options where id = 900229);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900230, 800057, 'Learning without any data', false
where not exists (select 1 from public.learning_quiz_options where id = 900230);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900231, 800057, 'Learning only from errors', false
where not exists (select 1 from public.learning_quiz_options where id = 900231);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800058, 2006, 'What is a convolutional neural network (CNN) primarily used for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800058);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900232, 800058, 'Text processing', false
where not exists (select 1 from public.learning_quiz_options where id = 900232);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900233, 800058, 'Image recognition and computer vision', true
where not exists (select 1 from public.learning_quiz_options where id = 900233);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900234, 800058, 'Time series forecasting', false
where not exists (select 1 from public.learning_quiz_options where id = 900234);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900235, 800058, 'Clustering', false
where not exists (select 1 from public.learning_quiz_options where id = 900235);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800059, 2006, 'What is the purpose of dropout in neural networks'''
where not exists (select 1 from public.learning_quiz_questions where id = 800059);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900236, 800059, 'Remove bad data', false
where not exists (select 1 from public.learning_quiz_options where id = 900236);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900237, 800059, 'Prevent overfitting by randomly dropping neurons during training', true
where not exists (select 1 from public.learning_quiz_options where id = 900237);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900238, 800059, 'Speed up training', false
where not exists (select 1 from public.learning_quiz_options where id = 900238);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900239, 800059, 'Reduce model size', false
where not exists (select 1 from public.learning_quiz_options where id = 900239);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800060, 2007, 'What does SEO stand for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800060);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900240, 800060, 'Social Engine Optimization', false
where not exists (select 1 from public.learning_quiz_options where id = 900240);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900241, 800060, 'Search Engine Optimization', true
where not exists (select 1 from public.learning_quiz_options where id = 900241);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900242, 800060, 'Simple Engine Operation', false
where not exists (select 1 from public.learning_quiz_options where id = 900242);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900243, 800060, 'Systematic Email Outreach', false
where not exists (select 1 from public.learning_quiz_options where id = 900243);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800061, 2007, 'Which metric measures the percentage of website visitors who leave after viewing only one page'''
where not exists (select 1 from public.learning_quiz_questions where id = 800061);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900244, 800061, 'Click-through rate', false
where not exists (select 1 from public.learning_quiz_options where id = 900244);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900245, 800061, 'Conversion rate', false
where not exists (select 1 from public.learning_quiz_options where id = 900245);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900246, 800061, 'Bounce rate', true
where not exists (select 1 from public.learning_quiz_options where id = 900246);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900247, 800061, 'Engagement rate', false
where not exists (select 1 from public.learning_quiz_options where id = 900247);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800062, 2007, 'What is a call-to-action (CTA)'''
where not exists (select 1 from public.learning_quiz_questions where id = 800062);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900248, 800062, 'A phone number', false
where not exists (select 1 from public.learning_quiz_options where id = 900248);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900249, 800062, 'A prompt encouraging users to take a specific action', true
where not exists (select 1 from public.learning_quiz_options where id = 900249);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900250, 800062, 'A customer complaint', false
where not exists (select 1 from public.learning_quiz_options where id = 900250);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900251, 800062, 'An advertising slogan', false
where not exists (select 1 from public.learning_quiz_options where id = 900251);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800063, 2007, 'Which platform is best for B2B marketing'''
where not exists (select 1 from public.learning_quiz_questions where id = 800063);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900252, 800063, 'Instagram', false
where not exists (select 1 from public.learning_quiz_options where id = 900252);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900253, 800063, 'TikTok', false
where not exists (select 1 from public.learning_quiz_options where id = 900253);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900254, 800063, 'LinkedIn', true
where not exists (select 1 from public.learning_quiz_options where id = 900254);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900255, 800063, 'Snapchat', false
where not exists (select 1 from public.learning_quiz_options where id = 900255);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800064, 2007, 'What does CTR stand for in digital advertising'''
where not exists (select 1 from public.learning_quiz_questions where id = 800064);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900256, 800064, 'Cost to Revenue', false
where not exists (select 1 from public.learning_quiz_options where id = 900256);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900257, 800064, 'Click-Through Rate', true
where not exists (select 1 from public.learning_quiz_options where id = 900257);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900258, 800064, 'Customer Target Range', false
where not exists (select 1 from public.learning_quiz_options where id = 900258);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900259, 800064, 'Content Tracking Report', false
where not exists (select 1 from public.learning_quiz_options where id = 900259);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800065, 2007, 'What is organic reach on social media'''
where not exists (select 1 from public.learning_quiz_questions where id = 800065);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900260, 800065, 'Reach from paid ads', false
where not exists (select 1 from public.learning_quiz_options where id = 900260);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900261, 800065, 'Reach from unpaid posts', true
where not exists (select 1 from public.learning_quiz_options where id = 900261);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900262, 800065, 'Total followers', false
where not exists (select 1 from public.learning_quiz_options where id = 900262);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900263, 800065, 'Engagement rate', false
where not exists (select 1 from public.learning_quiz_options where id = 900263);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800066, 2007, 'Which type of content typically generates the most engagement on social media'''
where not exists (select 1 from public.learning_quiz_questions where id = 800066);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900264, 800066, 'Long text posts', false
where not exists (select 1 from public.learning_quiz_options where id = 900264);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900265, 800066, 'Video content', true
where not exists (select 1 from public.learning_quiz_options where id = 900265);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900266, 800066, 'External links', false
where not exists (select 1 from public.learning_quiz_options where id = 900266);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900267, 800066, 'Plain images', false
where not exists (select 1 from public.learning_quiz_options where id = 900267);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800067, 2007, 'What is A/B testing in marketing'''
where not exists (select 1 from public.learning_quiz_questions where id = 800067);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900268, 800067, 'Testing two different products', false
where not exists (select 1 from public.learning_quiz_options where id = 900268);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900269, 800067, 'Comparing two versions to see which performs better', true
where not exists (select 1 from public.learning_quiz_options where id = 900269);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900270, 800067, 'Testing ad budgets', false
where not exists (select 1 from public.learning_quiz_options where id = 900270);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900271, 800067, 'Analyzing competitor strategies', false
where not exists (select 1 from public.learning_quiz_options where id = 900271);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800068, 2007, 'What does PPC stand for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800068);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900272, 800068, 'Pay Per Click', true
where not exists (select 1 from public.learning_quiz_options where id = 900272);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900273, 800068, 'Profit Per Customer', false
where not exists (select 1 from public.learning_quiz_options where id = 900273);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900274, 800068, 'Page Per Click', false
where not exists (select 1 from public.learning_quiz_options where id = 900274);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900275, 800068, 'Promotion Per Channel', false
where not exists (select 1 from public.learning_quiz_options where id = 900275);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800069, 2007, 'What is a landing page'''
where not exists (select 1 from public.learning_quiz_questions where id = 800069);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900276, 800069, 'The homepage of a website', false
where not exists (select 1 from public.learning_quiz_options where id = 900276);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900277, 800069, 'A page designed for a specific marketing campaign', true
where not exists (select 1 from public.learning_quiz_options where id = 900277);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900278, 800069, 'The last page a user visits', false
where not exists (select 1 from public.learning_quiz_options where id = 900278);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900279, 800069, 'A page with contact information', false
where not exists (select 1 from public.learning_quiz_options where id = 900279);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800070, 2008, 'What is the marketing funnel'''
where not exists (select 1 from public.learning_quiz_questions where id = 800070);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900280, 800070, 'A tool for email marketing', false
where not exists (select 1 from public.learning_quiz_options where id = 900280);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900281, 800070, 'The customer journey from awareness to purchase', true
where not exists (select 1 from public.learning_quiz_options where id = 900281);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900282, 800070, 'A type of advertisement', false
where not exists (select 1 from public.learning_quiz_options where id = 900282);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900283, 800070, 'A social media algorithm', false
where not exists (select 1 from public.learning_quiz_options where id = 900283);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800071, 2008, 'What is retargeting'''
where not exists (select 1 from public.learning_quiz_questions where id = 800071);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900284, 800071, 'Changing your target audience', false
where not exists (select 1 from public.learning_quiz_options where id = 900284);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900285, 800071, 'Showing ads to people who previously interacted with your brand', true
where not exists (select 1 from public.learning_quiz_options where id = 900285);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900286, 800071, 'Targeting competitors customers', false
where not exists (select 1 from public.learning_quiz_options where id = 900286);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900287, 800071, 'Setting new marketing goals', false
where not exists (select 1 from public.learning_quiz_options where id = 900287);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800072, 2008, 'What is the primary purpose of email segmentation'''
where not exists (select 1 from public.learning_quiz_questions where id = 800072);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900288, 800072, 'Reduce email size', false
where not exists (select 1 from public.learning_quiz_options where id = 900288);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900289, 800072, 'Send more relevant content to specific groups', true
where not exists (select 1 from public.learning_quiz_options where id = 900289);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900290, 800072, 'Comply with regulations', false
where not exists (select 1 from public.learning_quiz_options where id = 900290);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900291, 800072, 'Save money', false
where not exists (select 1 from public.learning_quiz_options where id = 900291);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800073, 2008, 'What is influencer marketing'''
where not exists (select 1 from public.learning_quiz_questions where id = 800073);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900292, 800073, 'Marketing to company executives', false
where not exists (select 1 from public.learning_quiz_options where id = 900292);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900293, 800073, 'Partnering with individuals who have social influence to promote products', true
where not exists (select 1 from public.learning_quiz_options where id = 900293);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900294, 800073, 'Traditional advertising', false
where not exists (select 1 from public.learning_quiz_options where id = 900294);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900295, 800073, 'Direct mail campaigns', false
where not exists (select 1 from public.learning_quiz_options where id = 900295);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800074, 2008, 'What does ROAS stand for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800074);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900296, 800074, 'Return On Ad Spend', true
where not exists (select 1 from public.learning_quiz_options where id = 900296);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900297, 800074, 'Rate Of Active Sales', false
where not exists (select 1 from public.learning_quiz_options where id = 900297);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900298, 800074, 'Reach Of Advertising Strategy', false
where not exists (select 1 from public.learning_quiz_options where id = 900298);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900299, 800074, 'Revenue Over Annual Sales', false
where not exists (select 1 from public.learning_quiz_options where id = 900299);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800075, 2008, 'What is content pillars in content marketing'''
where not exists (select 1 from public.learning_quiz_questions where id = 800075);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900300, 800075, 'The main topics your content focuses on', true
where not exists (select 1 from public.learning_quiz_options where id = 900300);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900301, 800075, 'Physical supports for displays', false
where not exists (select 1 from public.learning_quiz_options where id = 900301);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900302, 800075, 'Types of blog posts', false
where not exists (select 1 from public.learning_quiz_options where id = 900302);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900303, 800075, 'Social media platforms', false
where not exists (select 1 from public.learning_quiz_options where id = 900303);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800076, 2008, 'What is programmatic advertising'''
where not exists (select 1 from public.learning_quiz_questions where id = 800076);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900304, 800076, 'Manual ad buying', false
where not exists (select 1 from public.learning_quiz_options where id = 900304);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900305, 800076, 'Automated buying and selling of digital ad space', true
where not exists (select 1 from public.learning_quiz_options where id = 900305);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900306, 800076, 'TV advertising', false
where not exists (select 1 from public.learning_quiz_options where id = 900306);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900307, 800076, 'Print advertising', false
where not exists (select 1 from public.learning_quiz_options where id = 900307);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800077, 2008, 'What is the customer lifetime value (CLV)'''
where not exists (select 1 from public.learning_quiz_questions where id = 800077);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900308, 800077, 'Cost of customer acquisition', false
where not exists (select 1 from public.learning_quiz_options where id = 900308);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900309, 800077, 'Total revenue a business can expect from a single customer', true
where not exists (select 1 from public.learning_quiz_options where id = 900309);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900310, 800077, 'Average purchase amount', false
where not exists (select 1 from public.learning_quiz_options where id = 900310);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900311, 800077, 'Customer retention rate', false
where not exists (select 1 from public.learning_quiz_options where id = 900311);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800078, 2008, 'What is user-generated content (UGC)'''
where not exists (select 1 from public.learning_quiz_questions where id = 800078);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900312, 800078, 'Content created by the company', false
where not exists (select 1 from public.learning_quiz_options where id = 900312);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900313, 800078, 'Content created by customers or users', true
where not exists (select 1 from public.learning_quiz_options where id = 900313);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900314, 800078, 'Automated content', false
where not exists (select 1 from public.learning_quiz_options where id = 900314);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900315, 800078, 'Licensed content', false
where not exists (select 1 from public.learning_quiz_options where id = 900315);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800079, 2008, 'What is the purpose of a lead magnet'''
where not exists (select 1 from public.learning_quiz_questions where id = 800079);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900316, 800079, 'Attract competitors', false
where not exists (select 1 from public.learning_quiz_options where id = 900316);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900317, 800079, 'Offer value in exchange for contact information', true
where not exists (select 1 from public.learning_quiz_options where id = 900317);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900318, 800079, 'Increase website traffic', false
where not exists (select 1 from public.learning_quiz_options where id = 900318);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900319, 800079, 'Improve SEO', false
where not exists (select 1 from public.learning_quiz_options where id = 900319);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800080, 2009, 'What is marketing attribution modeling'''
where not exists (select 1 from public.learning_quiz_questions where id = 800080);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900320, 800080, 'Crediting sales to team members', false
where not exists (select 1 from public.learning_quiz_options where id = 900320);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900321, 800080, 'Determining which touchpoints deserve credit for conversions', true
where not exists (select 1 from public.learning_quiz_options where id = 900321);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900322, 800080, 'Tracking website visitors', false
where not exists (select 1 from public.learning_quiz_options where id = 900322);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900323, 800080, 'Measuring brand awareness', false
where not exists (select 1 from public.learning_quiz_options where id = 900323);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800081, 2009, 'What is the difference between first-party and third-party data'''
where not exists (select 1 from public.learning_quiz_questions where id = 800081);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900324, 800081, 'No difference', false
where not exists (select 1 from public.learning_quiz_options where id = 900324);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900325, 800081, 'First-party is collected directly from your audience, third-party is purchased', true
where not exists (select 1 from public.learning_quiz_options where id = 900325);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900326, 800081, 'First-party is free, third-party is paid', false
where not exists (select 1 from public.learning_quiz_options where id = 900326);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900327, 800081, 'Third-party is more accurate', false
where not exists (select 1 from public.learning_quiz_options where id = 900327);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800082, 2009, 'What is cohort analysis in marketing'''
where not exists (select 1 from public.learning_quiz_questions where id = 800082);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900328, 800082, 'Analyzing competitor strategies', false
where not exists (select 1 from public.learning_quiz_options where id = 900328);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900329, 800082, 'Grouping users based on shared characteristics and analyzing behavior over time', true
where not exists (select 1 from public.learning_quiz_options where id = 900329);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900330, 800082, 'Testing different ad creatives', false
where not exists (select 1 from public.learning_quiz_options where id = 900330);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900331, 800082, 'Segmenting email lists', false
where not exists (select 1 from public.learning_quiz_options where id = 900331);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800083, 2009, 'What is predictive analytics in marketing'''
where not exists (select 1 from public.learning_quiz_questions where id = 800083);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900332, 800083, 'Guessing future trends', false
where not exists (select 1 from public.learning_quiz_options where id = 900332);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900333, 800083, 'Using data and algorithms to forecast future outcomes', true
where not exists (select 1 from public.learning_quiz_options where id = 900333);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900334, 800083, 'Historical reporting', false
where not exists (select 1 from public.learning_quiz_options where id = 900334);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900335, 800083, 'A/B testing', false
where not exists (select 1 from public.learning_quiz_options where id = 900335);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800084, 2009, 'What is conversion rate optimization (CRO)'''
where not exists (select 1 from public.learning_quiz_questions where id = 800084);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900336, 800084, 'Increasing traffic', false
where not exists (select 1 from public.learning_quiz_options where id = 900336);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900337, 800084, 'Improving the percentage of visitors who complete desired actions', true
where not exists (select 1 from public.learning_quiz_options where id = 900337);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900338, 800084, 'Reducing costs', false
where not exists (select 1 from public.learning_quiz_options where id = 900338);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900339, 800084, 'Building brand awareness', false
where not exists (select 1 from public.learning_quiz_options where id = 900339);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800085, 2009, 'What is marketing automation'''
where not exists (select 1 from public.learning_quiz_questions where id = 800085);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900340, 800085, 'Replacing marketers with robots', false
where not exists (select 1 from public.learning_quiz_options where id = 900340);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900341, 800085, 'Software that automates repetitive marketing tasks', true
where not exists (select 1 from public.learning_quiz_options where id = 900341);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900342, 800085, 'Automatic social media posting', false
where not exists (select 1 from public.learning_quiz_options where id = 900342);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900343, 800085, 'AI-generated content', false
where not exists (select 1 from public.learning_quiz_options where id = 900343);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800086, 2009, 'What is account-based marketing (ABM)'''
where not exists (select 1 from public.learning_quiz_questions where id = 800086);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900344, 800086, 'Marketing to bank accounts', false
where not exists (select 1 from public.learning_quiz_options where id = 900344);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900345, 800086, 'Targeting specific high-value accounts with personalized campaigns', true
where not exists (select 1 from public.learning_quiz_options where id = 900345);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900346, 800086, 'Mass marketing', false
where not exists (select 1 from public.learning_quiz_options where id = 900346);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900347, 800086, 'Social media marketing', false
where not exists (select 1 from public.learning_quiz_options where id = 900347);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800087, 2009, 'What is multivariate testing'''
where not exists (select 1 from public.learning_quiz_questions where id = 800087);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900348, 800087, 'Testing multiple variables simultaneously', true
where not exists (select 1 from public.learning_quiz_options where id = 900348);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900349, 800087, 'Testing one variable at a time', false
where not exists (select 1 from public.learning_quiz_options where id = 900349);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900350, 800087, 'Testing different audiences', false
where not exists (select 1 from public.learning_quiz_options where id = 900350);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900351, 800087, 'Testing different platforms', false
where not exists (select 1 from public.learning_quiz_options where id = 900351);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800088, 2009, 'What is the purpose of customer journey mapping'''
where not exists (select 1 from public.learning_quiz_questions where id = 800088);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900352, 800088, 'Planning travel routes', false
where not exists (select 1 from public.learning_quiz_options where id = 900352);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900353, 800088, 'Visualizing all touchpoints a customer has with your brand', true
where not exists (select 1 from public.learning_quiz_options where id = 900353);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900354, 800088, 'Creating sales funnels', false
where not exists (select 1 from public.learning_quiz_options where id = 900354);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900355, 800088, 'Designing websites', false
where not exists (select 1 from public.learning_quiz_options where id = 900355);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800089, 2009, 'What is dynamic content in marketing'''
where not exists (select 1 from public.learning_quiz_questions where id = 800089);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900356, 800089, 'Video content', false
where not exists (select 1 from public.learning_quiz_options where id = 900356);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900357, 800089, 'Content that changes based on user data and behavior', true
where not exists (select 1 from public.learning_quiz_options where id = 900357);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900358, 800089, 'Live streaming', false
where not exists (select 1 from public.learning_quiz_options where id = 900358);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900359, 800089, 'Animated content', false
where not exists (select 1 from public.learning_quiz_options where id = 900359);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800090, 2010, 'What is a minimum viable product (MVP)'''
where not exists (select 1 from public.learning_quiz_questions where id = 800090);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900360, 800090, 'A perfect product with all features', false
where not exists (select 1 from public.learning_quiz_options where id = 900360);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900361, 800090, 'A product with minimum features to validate assumptions', true
where not exists (select 1 from public.learning_quiz_options where id = 900361);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900362, 800090, 'A failed product', false
where not exists (select 1 from public.learning_quiz_options where id = 900362);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900363, 800090, 'A prototype', false
where not exists (select 1 from public.learning_quiz_options where id = 900363);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800091, 2010, 'What does the term "product-market fit" mean'''
where not exists (select 1 from public.learning_quiz_questions where id = 800091);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900364, 800091, 'The product price matches market rates', false
where not exists (select 1 from public.learning_quiz_options where id = 900364);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900365, 800091, 'The product satisfies market demand', true
where not exists (select 1 from public.learning_quiz_options where id = 900365);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900366, 800091, 'The product design matches trends', false
where not exists (select 1 from public.learning_quiz_options where id = 900366);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900367, 800091, 'The product timeline matches deadlines', false
where not exists (select 1 from public.learning_quiz_options where id = 900367);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800092, 2010, 'What is a user story in product development'''
where not exists (select 1 from public.learning_quiz_questions where id = 800092);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900368, 800092, 'A customer testimonial', false
where not exists (select 1 from public.learning_quiz_options where id = 900368);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900369, 800092, 'A description of a feature from the user perspective', true
where not exists (select 1 from public.learning_quiz_options where id = 900369);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900370, 800092, 'A case study', false
where not exists (select 1 from public.learning_quiz_options where id = 900370);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900371, 800092, 'User documentation', false
where not exists (select 1 from public.learning_quiz_options where id = 900371);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800093, 2010, 'What is the purpose of user personas'''
where not exists (select 1 from public.learning_quiz_questions where id = 800093);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900372, 800093, 'To create fictional characters', false
where not exists (select 1 from public.learning_quiz_options where id = 900372);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900373, 800093, 'To represent different user types and their needs', true
where not exists (select 1 from public.learning_quiz_options where id = 900373);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900374, 800093, 'To test product names', false
where not exists (select 1 from public.learning_quiz_options where id = 900374);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900375, 800093, 'To design logos', false
where not exists (select 1 from public.learning_quiz_options where id = 900375);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800094, 2010, 'What does "feature creep" mean'''
where not exists (select 1 from public.learning_quiz_questions where id = 800094);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900376, 800094, 'Slow development', false
where not exists (select 1 from public.learning_quiz_options where id = 900376);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900377, 800094, 'Continuously adding features beyond initial scope', true
where not exists (select 1 from public.learning_quiz_options where id = 900377);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900378, 800094, 'Copying competitor features', false
where not exists (select 1 from public.learning_quiz_options where id = 900378);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900379, 800094, 'Removing features', false
where not exists (select 1 from public.learning_quiz_options where id = 900379);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800095, 2010, 'What is a product roadmap'''
where not exists (select 1 from public.learning_quiz_questions where id = 800095);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900380, 800095, 'A map of product locations', false
where not exists (select 1 from public.learning_quiz_options where id = 900380);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900381, 800095, 'A strategic plan showing product direction and timeline', true
where not exists (select 1 from public.learning_quiz_options where id = 900381);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900382, 800095, 'A user journey', false
where not exists (select 1 from public.learning_quiz_options where id = 900382);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900383, 800095, 'A technical diagram', false
where not exists (select 1 from public.learning_quiz_options where id = 900383);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800096, 2010, 'What is the main goal of usability testing'''
where not exists (select 1 from public.learning_quiz_questions where id = 800096);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900384, 800096, 'Test product durability', false
where not exists (select 1 from public.learning_quiz_options where id = 900384);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900385, 800096, 'Evaluate how easy and intuitive a product is to use', true
where not exists (select 1 from public.learning_quiz_options where id = 900385);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900386, 800096, 'Check for bugs', false
where not exists (select 1 from public.learning_quiz_options where id = 900386);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900387, 800096, 'Measure performance', false
where not exists (select 1 from public.learning_quiz_options where id = 900387);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800097, 2010, 'What is a sprint in Agile development'''
where not exists (select 1 from public.learning_quiz_questions where id = 800097);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900388, 800097, 'A fast runner', false
where not exists (select 1 from public.learning_quiz_options where id = 900388);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900389, 800097, 'A time-boxed period to complete specific work', true
where not exists (select 1 from public.learning_quiz_options where id = 900389);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900390, 800097, 'A product launch', false
where not exists (select 1 from public.learning_quiz_options where id = 900390);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900391, 800097, 'A team meeting', false
where not exists (select 1 from public.learning_quiz_options where id = 900391);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800098, 2010, 'What does "backlog" mean in product management'''
where not exists (select 1 from public.learning_quiz_questions where id = 800098);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900392, 800098, 'Old products', false
where not exists (select 1 from public.learning_quiz_options where id = 900392);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900393, 800098, 'A prioritized list of features and tasks to be completed', true
where not exists (select 1 from public.learning_quiz_options where id = 900393);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900394, 800098, 'Delayed shipments', false
where not exists (select 1 from public.learning_quiz_options where id = 900394);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900395, 800098, 'Past projects', false
where not exists (select 1 from public.learning_quiz_options where id = 900395);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800099, 2010, 'What is the purpose of a product launch'''
where not exists (select 1 from public.learning_quiz_questions where id = 800099);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900396, 800099, 'End product development', false
where not exists (select 1 from public.learning_quiz_options where id = 900396);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900397, 800099, 'Introduce a new product to the market', true
where not exists (select 1 from public.learning_quiz_options where id = 900397);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900398, 800099, 'Close a project', false
where not exists (select 1 from public.learning_quiz_options where id = 900398);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900399, 800099, 'Start hiring', false
where not exists (select 1 from public.learning_quiz_options where id = 900399);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800100, 2011, 'What is the RICE prioritization framework'''
where not exists (select 1 from public.learning_quiz_questions where id = 800100);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900400, 800100, 'A food-based method', false
where not exists (select 1 from public.learning_quiz_options where id = 900400);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900401, 800100, 'Reach, Impact, Confidence, Effort scoring method', true
where not exists (select 1 from public.learning_quiz_options where id = 900401);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900402, 800100, 'A testing framework', false
where not exists (select 1 from public.learning_quiz_options where id = 900402);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900403, 800100, 'A design system', false
where not exists (select 1 from public.learning_quiz_options where id = 900403);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800101, 2011, 'What is the purpose of A/B testing in product management'''
where not exists (select 1 from public.learning_quiz_questions where id = 800101);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900404, 800101, 'Testing two products', false
where not exists (select 1 from public.learning_quiz_options where id = 900404);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900405, 800101, 'Comparing two versions to determine which performs better', true
where not exists (select 1 from public.learning_quiz_options where id = 900405);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900406, 800101, 'Testing team members', false
where not exists (select 1 from public.learning_quiz_options where id = 900406);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900407, 800101, 'Budget allocation', false
where not exists (select 1 from public.learning_quiz_options where id = 900407);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800102, 2011, 'What is the Jobs-to-be-Done framework'''
where not exists (select 1 from public.learning_quiz_questions where id = 800102);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900408, 800102, 'Employee management', false
where not exists (select 1 from public.learning_quiz_options where id = 900408);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900409, 800102, 'Understanding what customers are trying to accomplish', true
where not exists (select 1 from public.learning_quiz_options where id = 900409);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900410, 800102, 'Task management', false
where not exists (select 1 from public.learning_quiz_options where id = 900410);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900411, 800102, 'Job descriptions', false
where not exists (select 1 from public.learning_quiz_options where id = 900411);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800103, 2011, 'What is customer churn'''
where not exists (select 1 from public.learning_quiz_questions where id = 800103);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900412, 800103, 'Customer satisfaction', false
where not exists (select 1 from public.learning_quiz_options where id = 900412);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900413, 800103, 'The rate at which customers stop using your product', true
where not exists (select 1 from public.learning_quiz_options where id = 900413);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900414, 800103, 'Customer acquisition', false
where not exists (select 1 from public.learning_quiz_options where id = 900414);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900415, 800103, 'Customer feedback', false
where not exists (select 1 from public.learning_quiz_options where id = 900415);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800104, 2011, 'What is the purpose of OKRs (Objectives and Key Results)'''
where not exists (select 1 from public.learning_quiz_questions where id = 800104);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900416, 800104, 'Performance reviews', false
where not exists (select 1 from public.learning_quiz_options where id = 900416);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900417, 800104, 'Set and track measurable goals', true
where not exists (select 1 from public.learning_quiz_options where id = 900417);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900418, 800104, 'Budget planning', false
where not exists (select 1 from public.learning_quiz_options where id = 900418);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900419, 800104, 'Resource allocation', false
where not exists (select 1 from public.learning_quiz_options where id = 900419);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800105, 2011, 'What is the Kano Model used for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800105);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900420, 800105, 'Financial modeling', false
where not exists (select 1 from public.learning_quiz_options where id = 900420);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900421, 800105, 'Categorizing features based on customer satisfaction', true
where not exists (select 1 from public.learning_quiz_options where id = 900421);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900422, 800105, 'Market sizing', false
where not exists (select 1 from public.learning_quiz_options where id = 900422);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900423, 800105, 'Competitive analysis', false
where not exists (select 1 from public.learning_quiz_options where id = 900423);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800106, 2011, 'What is technical debt'''
where not exists (select 1 from public.learning_quiz_questions where id = 800106);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900424, 800106, 'Money owed to engineers', false
where not exists (select 1 from public.learning_quiz_options where id = 900424);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900425, 800106, 'The cost of additional work caused by choosing quick solutions', true
where not exists (select 1 from public.learning_quiz_options where id = 900425);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900426, 800106, 'Budget overruns', false
where not exists (select 1 from public.learning_quiz_options where id = 900426);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900427, 800106, 'Delayed payments', false
where not exists (select 1 from public.learning_quiz_options where id = 900427);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800107, 2011, 'What is the purpose of customer journey mapping'''
where not exists (select 1 from public.learning_quiz_questions where id = 800107);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900428, 800107, 'Creating maps', false
where not exists (select 1 from public.learning_quiz_options where id = 900428);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900429, 800107, 'Visualizing customer interactions across touchpoints', true
where not exists (select 1 from public.learning_quiz_options where id = 900429);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900430, 800107, 'Planning travel', false
where not exists (select 1 from public.learning_quiz_options where id = 900430);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900431, 800107, 'Designing interfaces', false
where not exists (select 1 from public.learning_quiz_options where id = 900431);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800108, 2011, 'What is cohort analysis'''
where not exists (select 1 from public.learning_quiz_questions where id = 800108);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900432, 800108, 'Group therapy', false
where not exists (select 1 from public.learning_quiz_options where id = 900432);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900433, 800108, 'Analyzing groups of users with shared characteristics over time', true
where not exists (select 1 from public.learning_quiz_options where id = 900433);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900434, 800108, 'Team building', false
where not exists (select 1 from public.learning_quiz_options where id = 900434);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900435, 800108, 'Market segmentation', false
where not exists (select 1 from public.learning_quiz_options where id = 900435);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800109, 2011, 'What is the North Star Metric'''
where not exists (select 1 from public.learning_quiz_questions where id = 800109);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900436, 800109, 'Navigation tool', false
where not exists (select 1 from public.learning_quiz_options where id = 900436);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900437, 800109, 'The single metric that best captures the core value your product delivers', true
where not exists (select 1 from public.learning_quiz_options where id = 900437);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900438, 800109, 'Financial target', false
where not exists (select 1 from public.learning_quiz_options where id = 900438);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900439, 800109, 'User count', false
where not exists (select 1 from public.learning_quiz_options where id = 900439);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800110, 2012, 'What is platform thinking in product strategy'''
where not exists (select 1 from public.learning_quiz_questions where id = 800110);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900440, 800110, 'Choosing tech platforms', false
where not exists (select 1 from public.learning_quiz_options where id = 900440);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900441, 800110, 'Building products that enable ecosystems and third-party value creation', true
where not exists (select 1 from public.learning_quiz_options where id = 900441);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900442, 800110, 'Social media strategy', false
where not exists (select 1 from public.learning_quiz_options where id = 900442);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900443, 800110, 'Cloud migration', false
where not exists (select 1 from public.learning_quiz_options where id = 900443);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800111, 2012, 'What is the concept of "crossing the chasm"'''
where not exists (select 1 from public.learning_quiz_questions where id = 800111);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900444, 800111, 'Jumping obstacles', false
where not exists (select 1 from public.learning_quiz_options where id = 900444);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900445, 800111, 'Transitioning from early adopters to mainstream market', true
where not exists (select 1 from public.learning_quiz_options where id = 900445);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900446, 800111, 'International expansion', false
where not exists (select 1 from public.learning_quiz_options where id = 900446);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900447, 800111, 'Competitive advantage', false
where not exists (select 1 from public.learning_quiz_options where id = 900447);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800112, 2012, 'What is blue ocean strategy'''
where not exists (select 1 from public.learning_quiz_questions where id = 800112);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900448, 800112, 'Maritime business', false
where not exists (select 1 from public.learning_quiz_options where id = 900448);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900449, 800112, 'Creating uncontested market space by innovation', true
where not exists (select 1 from public.learning_quiz_options where id = 900449);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900450, 800112, 'Aggressive competition', false
where not exists (select 1 from public.learning_quiz_options where id = 900450);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900451, 800112, 'Price reduction', false
where not exists (select 1 from public.learning_quiz_options where id = 900451);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800113, 2012, 'What is the concept of "product-led growth"'''
where not exists (select 1 from public.learning_quiz_questions where id = 800113);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900452, 800113, 'Product team leads company', false
where not exists (select 1 from public.learning_quiz_options where id = 900452);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900453, 800113, 'Using the product itself as the primary driver of customer acquisition', true
where not exists (select 1 from public.learning_quiz_options where id = 900453);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900454, 800113, 'Focusing on product features', false
where not exists (select 1 from public.learning_quiz_options where id = 900454);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900455, 800113, 'Engineering-driven culture', false
where not exists (select 1 from public.learning_quiz_options where id = 900455);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800114, 2012, 'What is the purpose of a TAM/SAM/SOM analysis'''
where not exists (select 1 from public.learning_quiz_questions where id = 800114);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900456, 800114, 'Team assessment', false
where not exists (select 1 from public.learning_quiz_options where id = 900456);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900457, 800114, 'Estimating total addressable, serviceable, and obtainable market', true
where not exists (select 1 from public.learning_quiz_options where id = 900457);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900458, 800114, 'Technology selection', false
where not exists (select 1 from public.learning_quiz_options where id = 900458);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900459, 800114, 'Budget planning', false
where not exists (select 1 from public.learning_quiz_options where id = 900459);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800115, 2012, 'What is the concept of "network effects"'''
where not exists (select 1 from public.learning_quiz_questions where id = 800115);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900460, 800115, 'Internet connectivity', false
where not exists (select 1 from public.learning_quiz_options where id = 900460);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900461, 800115, 'Product value increases as more people use it', true
where not exists (select 1 from public.learning_quiz_options where id = 900461);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900462, 800115, 'Team collaboration', false
where not exists (select 1 from public.learning_quiz_options where id = 900462);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900463, 800115, 'Marketing reach', false
where not exists (select 1 from public.learning_quiz_options where id = 900463);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800116, 2012, 'What is the Three Horizons model'''
where not exists (select 1 from public.learning_quiz_questions where id = 800116);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900464, 800116, 'Time zones', false
where not exists (select 1 from public.learning_quiz_options where id = 900464);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900465, 800116, 'Framework for balancing current, emerging, and future opportunities', true
where not exists (select 1 from public.learning_quiz_options where id = 900465);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900466, 800116, 'Planning cycles', false
where not exists (select 1 from public.learning_quiz_options where id = 900466);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900467, 800116, 'Skill levels', false
where not exists (select 1 from public.learning_quiz_options where id = 900467);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800117, 2012, 'What is the purpose of scenario planning'''
where not exists (select 1 from public.learning_quiz_questions where id = 800117);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900468, 800117, 'Event management', false
where not exists (select 1 from public.learning_quiz_options where id = 900468);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900469, 800117, 'Preparing for multiple possible futures', true
where not exists (select 1 from public.learning_quiz_options where id = 900469);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900470, 800117, 'User testing', false
where not exists (select 1 from public.learning_quiz_options where id = 900470);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900471, 800117, 'Sprint planning', false
where not exists (select 1 from public.learning_quiz_options where id = 900471);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800118, 2012, 'What is the concept of "moats" in product strategy'''
where not exists (select 1 from public.learning_quiz_questions where id = 800118);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900472, 800118, 'Water features', false
where not exists (select 1 from public.learning_quiz_options where id = 900472);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900473, 800118, 'Sustainable competitive advantages that protect market position', true
where not exists (select 1 from public.learning_quiz_options where id = 900473);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900474, 800118, 'Barriers to entry', false
where not exists (select 1 from public.learning_quiz_options where id = 900474);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900475, 800118, 'Patent protection', false
where not exists (select 1 from public.learning_quiz_options where id = 900475);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800119, 2012, 'What is the Ansoff Matrix used for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800119);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900476, 800119, 'Data visualization', false
where not exists (select 1 from public.learning_quiz_options where id = 900476);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900477, 800119, 'Identifying growth strategies based on products and markets', true
where not exists (select 1 from public.learning_quiz_options where id = 900477);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900478, 800119, 'Competitive analysis', false
where not exists (select 1 from public.learning_quiz_options where id = 900478);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900479, 800119, 'Risk assessment', false
where not exists (select 1 from public.learning_quiz_options where id = 900479);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800120, 2013, 'What is emotional intelligence in leadership'''
where not exists (select 1 from public.learning_quiz_questions where id = 800120);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900480, 800120, 'Being emotional', false
where not exists (select 1 from public.learning_quiz_options where id = 900480);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900481, 800120, 'The ability to recognize and manage emotions in yourself and others', true
where not exists (select 1 from public.learning_quiz_options where id = 900481);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900482, 800120, 'IQ for emotions', false
where not exists (select 1 from public.learning_quiz_options where id = 900482);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900483, 800120, 'Empathy only', false
where not exists (select 1 from public.learning_quiz_options where id = 900483);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800121, 2013, 'What is delegation in management'''
where not exists (select 1 from public.learning_quiz_questions where id = 800121);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900484, 800121, 'Avoiding work', false
where not exists (select 1 from public.learning_quiz_options where id = 900484);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900485, 800121, 'Assigning responsibility and authority to team members', true
where not exists (select 1 from public.learning_quiz_options where id = 900485);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900486, 800121, 'Micromanagement', false
where not exists (select 1 from public.learning_quiz_options where id = 900486);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900487, 800121, 'Task elimination', false
where not exists (select 1 from public.learning_quiz_options where id = 900487);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800122, 2013, 'What is active listening'''
where not exists (select 1 from public.learning_quiz_questions where id = 800122);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900488, 800122, 'Listening to music while working', false
where not exists (select 1 from public.learning_quiz_options where id = 900488);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900489, 800122, 'Fully concentrating and understanding what is being said', true
where not exists (select 1 from public.learning_quiz_options where id = 900489);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900490, 800122, 'Hearing words', false
where not exists (select 1 from public.learning_quiz_options where id = 900490);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900491, 800122, 'Taking notes', false
where not exists (select 1 from public.learning_quiz_options where id = 900491);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800123, 2013, 'What is a SMART goal'''
where not exists (select 1 from public.learning_quiz_questions where id = 800123);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900492, 800123, 'An intelligent objective', false
where not exists (select 1 from public.learning_quiz_options where id = 900492);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900493, 800123, 'Specific, Measurable, Achievable, Relevant, Time-bound', true
where not exists (select 1 from public.learning_quiz_options where id = 900493);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900494, 800123, 'A simple goal', false
where not exists (select 1 from public.learning_quiz_options where id = 900494);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900495, 800123, 'A strategic plan', false
where not exists (select 1 from public.learning_quiz_options where id = 900495);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800124, 2013, 'What is constructive feedback'''
where not exists (select 1 from public.learning_quiz_questions where id = 800124);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900496, 800124, 'Only positive comments', false
where not exists (select 1 from public.learning_quiz_options where id = 900496);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900497, 800124, 'Feedback aimed at improvement with specific, actionable suggestions', true
where not exists (select 1 from public.learning_quiz_options where id = 900497);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900498, 800124, 'Criticism', false
where not exists (select 1 from public.learning_quiz_options where id = 900498);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900499, 800124, 'Annual reviews only', false
where not exists (select 1 from public.learning_quiz_options where id = 900499);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800125, 2013, 'What is team diversity'''
where not exists (select 1 from public.learning_quiz_questions where id = 800125);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900500, 800125, 'Different uniforms', false
where not exists (select 1 from public.learning_quiz_options where id = 900500);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900501, 800125, 'Having team members with varied backgrounds, perspectives, and skills', true
where not exists (select 1 from public.learning_quiz_options where id = 900501);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900502, 800125, 'Large teams', false
where not exists (select 1 from public.learning_quiz_options where id = 900502);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900503, 800125, 'International teams', false
where not exists (select 1 from public.learning_quiz_options where id = 900503);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800126, 2013, 'What is a growth mindset'''
where not exists (select 1 from public.learning_quiz_questions where id = 800126);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900504, 800126, 'Focus on revenue', false
where not exists (select 1 from public.learning_quiz_options where id = 900504);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900505, 800126, 'Believing abilities can be developed through effort', true
where not exists (select 1 from public.learning_quiz_options where id = 900505);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900506, 800126, 'Optimism', false
where not exists (select 1 from public.learning_quiz_options where id = 900506);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900507, 800126, 'Ambition', false
where not exists (select 1 from public.learning_quiz_options where id = 900507);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800127, 2013, 'What is conflict resolution'''
where not exists (select 1 from public.learning_quiz_questions where id = 800127);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900508, 800127, 'Avoiding conflicts', false
where not exists (select 1 from public.learning_quiz_options where id = 900508);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900509, 800127, 'The process of resolving disputes in a constructive way', true
where not exists (select 1 from public.learning_quiz_options where id = 900509);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900510, 800127, 'Winning arguments', false
where not exists (select 1 from public.learning_quiz_options where id = 900510);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900511, 800127, 'Ignoring problems', false
where not exists (select 1 from public.learning_quiz_options where id = 900511);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800128, 2013, 'What is accountability in leadership'''
where not exists (select 1 from public.learning_quiz_questions where id = 800128);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900512, 800128, 'Keeping records', false
where not exists (select 1 from public.learning_quiz_options where id = 900512);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900513, 800128, 'Taking responsibility for decisions and outcomes', true
where not exists (select 1 from public.learning_quiz_options where id = 900513);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900514, 800128, 'Blame assignment', false
where not exists (select 1 from public.learning_quiz_options where id = 900514);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900515, 800128, 'Financial tracking', false
where not exists (select 1 from public.learning_quiz_options where id = 900515);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800129, 2013, 'What is the purpose of team building'''
where not exists (select 1 from public.learning_quiz_questions where id = 800129);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900516, 800129, 'Social events', false
where not exists (select 1 from public.learning_quiz_options where id = 900516);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900517, 800129, 'Improving collaboration and trust among team members', true
where not exists (select 1 from public.learning_quiz_options where id = 900517);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900518, 800129, 'Entertainment', false
where not exists (select 1 from public.learning_quiz_options where id = 900518);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900519, 800129, 'Budget spending', false
where not exists (select 1 from public.learning_quiz_options where id = 900519);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800130, 2014, 'What is transformational leadership'''
where not exists (select 1 from public.learning_quiz_questions where id = 800130);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900520, 800130, 'Changing jobs', false
where not exists (select 1 from public.learning_quiz_options where id = 900520);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900521, 800130, 'Inspiring and motivating team members to exceed expectations', true
where not exists (select 1 from public.learning_quiz_options where id = 900521);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900522, 800130, 'Organizational restructuring', false
where not exists (select 1 from public.learning_quiz_options where id = 900522);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900523, 800130, 'Technology adoption', false
where not exists (select 1 from public.learning_quiz_options where id = 900523);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800131, 2014, 'What is the purpose of a SWOT analysis'''
where not exists (select 1 from public.learning_quiz_questions where id = 800131);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900524, 800131, 'Team evaluation', false
where not exists (select 1 from public.learning_quiz_options where id = 900524);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900525, 800131, 'Identifying Strengths, Weaknesses, Opportunities, and Threats', true
where not exists (select 1 from public.learning_quiz_options where id = 900525);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900526, 800131, 'Financial planning', false
where not exists (select 1 from public.learning_quiz_options where id = 900526);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900527, 800131, 'Market research', false
where not exists (select 1 from public.learning_quiz_options where id = 900527);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800132, 2014, 'What is servant leadership'''
where not exists (select 1 from public.learning_quiz_questions where id = 800132);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900528, 800132, 'Being subservient', false
where not exists (select 1 from public.learning_quiz_options where id = 900528);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900529, 800132, 'Prioritizing the needs of team members and helping them develop', true
where not exists (select 1 from public.learning_quiz_options where id = 900529);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900530, 800132, 'Customer service', false
where not exists (select 1 from public.learning_quiz_options where id = 900530);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900531, 800132, 'Support roles', false
where not exists (select 1 from public.learning_quiz_options where id = 900531);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800133, 2014, 'What is change management'''
where not exists (select 1 from public.learning_quiz_questions where id = 800133);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900532, 800133, 'Currency exchange', false
where not exists (select 1 from public.learning_quiz_options where id = 900532);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900533, 800133, 'The structured approach to transitioning organizations to a desired state', true
where not exists (select 1 from public.learning_quiz_options where id = 900533);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900534, 800133, 'Editing documents', false
where not exists (select 1 from public.learning_quiz_options where id = 900534);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900535, 800133, 'Personnel changes', false
where not exists (select 1 from public.learning_quiz_options where id = 900535);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800134, 2014, 'What is stakeholder management'''
where not exists (select 1 from public.learning_quiz_questions where id = 800134);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900536, 800134, 'Investment management', false
where not exists (select 1 from public.learning_quiz_options where id = 900536);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900537, 800134, 'Identifying and addressing the needs of people affected by decisions', true
where not exists (select 1 from public.learning_quiz_options where id = 900537);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900538, 800134, 'Stock trading', false
where not exists (select 1 from public.learning_quiz_options where id = 900538);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900539, 800134, 'Customer service', false
where not exists (select 1 from public.learning_quiz_options where id = 900539);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800135, 2014, 'What is the Eisenhower Matrix used for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800135);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900540, 800135, 'Military strategy', false
where not exists (select 1 from public.learning_quiz_options where id = 900540);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900541, 800135, 'Prioritizing tasks based on urgency and importance', true
where not exists (select 1 from public.learning_quiz_options where id = 900541);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900542, 800135, 'Team organization', false
where not exists (select 1 from public.learning_quiz_options where id = 900542);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900543, 800135, 'Budget allocation', false
where not exists (select 1 from public.learning_quiz_options where id = 900543);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800136, 2014, 'What is psychological safety in teams'''
where not exists (select 1 from public.learning_quiz_questions where id = 800136);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900544, 800136, 'Physical security', false
where not exists (select 1 from public.learning_quiz_options where id = 900544);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900545, 800136, 'Feeling safe to take risks and be vulnerable without fear of punishment', true
where not exists (select 1 from public.learning_quiz_options where id = 900545);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900546, 800136, 'Mental health', false
where not exists (select 1 from public.learning_quiz_options where id = 900546);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900547, 800136, 'Insurance coverage', false
where not exists (select 1 from public.learning_quiz_options where id = 900547);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800137, 2014, 'What is strategic alignment'''
where not exists (select 1 from public.learning_quiz_questions where id = 800137);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900548, 800137, 'Perfect organization', false
where not exists (select 1 from public.learning_quiz_options where id = 900548);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900549, 800137, 'Ensuring all activities support overarching organizational goals', true
where not exists (select 1 from public.learning_quiz_options where id = 900549);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900550, 800137, 'Team agreement', false
where not exists (select 1 from public.learning_quiz_options where id = 900550);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900551, 800137, 'Resource distribution', false
where not exists (select 1 from public.learning_quiz_options where id = 900551);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800138, 2014, 'What is the concept of "leading by example"'''
where not exists (select 1 from public.learning_quiz_questions where id = 800138);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900552, 800138, 'Giving examples', false
where not exists (select 1 from public.learning_quiz_options where id = 900552);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900553, 800138, 'Demonstrating desired behaviors and values through your own actions', true
where not exists (select 1 from public.learning_quiz_options where id = 900553);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900554, 800138, 'Case studies', false
where not exists (select 1 from public.learning_quiz_options where id = 900554);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900555, 800138, 'Training others', false
where not exists (select 1 from public.learning_quiz_options where id = 900555);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800139, 2014, 'What is organizational culture'''
where not exists (select 1 from public.learning_quiz_questions where id = 800139);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900556, 800139, 'Arts programs', false
where not exists (select 1 from public.learning_quiz_options where id = 900556);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900557, 800139, 'Shared values, beliefs, and practices that shape how work gets done', true
where not exists (select 1 from public.learning_quiz_options where id = 900557);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900558, 800139, 'Company events', false
where not exists (select 1 from public.learning_quiz_options where id = 900558);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900559, 800139, 'Office design', false
where not exists (select 1 from public.learning_quiz_options where id = 900559);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800140, 2015, 'What is strategic foresight'''
where not exists (select 1 from public.learning_quiz_questions where id = 800140);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900560, 800140, 'Good vision', false
where not exists (select 1 from public.learning_quiz_options where id = 900560);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900561, 800140, 'Systematically thinking about possible futures to inform strategy', true
where not exists (select 1 from public.learning_quiz_options where id = 900561);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900562, 800140, 'Planning ahead', false
where not exists (select 1 from public.learning_quiz_options where id = 900562);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900563, 800140, 'Trend following', false
where not exists (select 1 from public.learning_quiz_options where id = 900563);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800141, 2015, 'What is the concept of "creative destruction" in strategy'''
where not exists (select 1 from public.learning_quiz_questions where id = 800141);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900564, 800141, 'Breaking things', false
where not exists (select 1 from public.learning_quiz_options where id = 900564);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900565, 800141, 'Innovation makes existing products/processes obsolete', true
where not exists (select 1 from public.learning_quiz_options where id = 900565);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900566, 800141, 'Brainstorming', false
where not exists (select 1 from public.learning_quiz_options where id = 900566);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900567, 800141, 'Disruptive behavior', false
where not exists (select 1 from public.learning_quiz_options where id = 900567);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800142, 2015, 'What is the purpose of scenario planning in leadership'''
where not exists (select 1 from public.learning_quiz_questions where id = 800142);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900568, 800142, 'Event planning', false
where not exists (select 1 from public.learning_quiz_options where id = 900568);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900569, 800142, 'Preparing for multiple plausible futures', true
where not exists (select 1 from public.learning_quiz_options where id = 900569);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900570, 800142, 'Crisis management', false
where not exists (select 1 from public.learning_quiz_options where id = 900570);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900571, 800142, 'Training exercises', false
where not exists (select 1 from public.learning_quiz_options where id = 900571);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800143, 2015, 'What is the concept of "ambidextrous organization"'''
where not exists (select 1 from public.learning_quiz_questions where id = 800143);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900572, 800143, 'Dual headquarters', false
where not exists (select 1 from public.learning_quiz_options where id = 900572);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900573, 800143, 'Balancing exploitation of existing capabilities with exploration of new opportunities', true
where not exists (select 1 from public.learning_quiz_options where id = 900573);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900574, 800143, 'Two CEOs', false
where not exists (select 1 from public.learning_quiz_options where id = 900574);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900575, 800143, 'Matrix structure', false
where not exists (select 1 from public.learning_quiz_options where id = 900575);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800144, 2015, 'What is the resource-based view of strategy'''
where not exists (select 1 from public.learning_quiz_questions where id = 800144);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900576, 800144, 'Budget management', false
where not exists (select 1 from public.learning_quiz_options where id = 900576);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900577, 800144, 'Competitive advantage comes from unique, valuable resources and capabilities', true
where not exists (select 1 from public.learning_quiz_options where id = 900577);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900578, 800144, 'Asset allocation', false
where not exists (select 1 from public.learning_quiz_options where id = 900578);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900579, 800144, 'HR planning', false
where not exists (select 1 from public.learning_quiz_options where id = 900579);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800145, 2015, 'What is the concept of "dynamic capabilities"'''
where not exists (select 1 from public.learning_quiz_questions where id = 800145);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900580, 800145, 'Multiple skills', false
where not exists (select 1 from public.learning_quiz_options where id = 900580);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900581, 800145, 'The ability to sense, seize, and transform to adapt to change', true
where not exists (select 1 from public.learning_quiz_options where id = 900581);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900582, 800145, 'Physical agility', false
where not exists (select 1 from public.learning_quiz_options where id = 900582);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900583, 800145, 'Flexible scheduling', false
where not exists (select 1 from public.learning_quiz_options where id = 900583);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800146, 2015, 'What is the purpose of a balanced scorecard'''
where not exists (select 1 from public.learning_quiz_questions where id = 800146);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900584, 800146, 'Sports metrics', false
where not exists (select 1 from public.learning_quiz_options where id = 900584);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900585, 800146, 'Measuring performance across financial, customer, internal, and learning perspectives', true
where not exists (select 1 from public.learning_quiz_options where id = 900585);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900586, 800146, 'Budget tracking', false
where not exists (select 1 from public.learning_quiz_options where id = 900586);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900587, 800146, 'Employee evaluation', false
where not exists (select 1 from public.learning_quiz_options where id = 900587);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800147, 2015, 'What is the concept of "blue ocean strategy"'''
where not exists (select 1 from public.learning_quiz_questions where id = 800147);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900588, 800147, 'Ocean conservation', false
where not exists (select 1 from public.learning_quiz_options where id = 900588);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900589, 800147, 'Creating uncontested market space rather than competing in existing markets', true
where not exists (select 1 from public.learning_quiz_options where id = 900589);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900590, 800147, 'Maritime industry', false
where not exists (select 1 from public.learning_quiz_options where id = 900590);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900591, 800147, 'Environmental strategy', false
where not exists (select 1 from public.learning_quiz_options where id = 900591);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800148, 2015, 'What is the five forces framework used for'''
where not exists (select 1 from public.learning_quiz_questions where id = 800148);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900592, 800148, 'Military analysis', false
where not exists (select 1 from public.learning_quiz_options where id = 900592);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900593, 800148, 'Analyzing industry competitive forces to determine attractiveness', true
where not exists (select 1 from public.learning_quiz_options where id = 900593);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900594, 800148, 'Team dynamics', false
where not exists (select 1 from public.learning_quiz_options where id = 900594);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900595, 800148, 'Project management', false
where not exists (select 1 from public.learning_quiz_options where id = 900595);
insert into public.learning_quiz_questions (id, learning_module_id, question)
select 800149, 2015, 'What is the concept of "strategic inflection point"'''
where not exists (select 1 from public.learning_quiz_questions where id = 800149);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900596, 800149, 'Turning point', false
where not exists (select 1 from public.learning_quiz_options where id = 900596);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900597, 800149, 'A time when fundamental change in business is occurring', true
where not exists (select 1 from public.learning_quiz_options where id = 900597);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900598, 800149, 'Breaking point', false
where not exists (select 1 from public.learning_quiz_options where id = 900598);
insert into public.learning_quiz_options (id, quiz_question_id, text, is_correct)
select 900599, 800149, 'Decision moment', false
where not exists (select 1 from public.learning_quiz_options where id = 900599);

-- 5) Achievement catalog (new table for content-driven achievements)
create table if not exists public.reward_achievement_catalog (
  achievement_id text primary key,
  name text not null,
  description text not null,
  points int not null default 0,
  unlock_criteria text,
  icon_name text,
  category text,
  created_at timestamptz not null default now()
);
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH001', 'First Steps', 'Complete your first learning module', 50, 'Complete 1 module', 'trophy', 'Learning')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH002', 'Knowledge Seeker', 'Complete 5 learning modules', 150, 'Complete 5 modules', 'book', 'Learning')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH003', 'Scholar', 'Complete 15 learning modules', 500, 'Complete 15 modules', 'graduation-cap', 'Learning')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH004', 'Master Learner', 'Pass all modules across all topics', 1000, 'Pass all modules', 'crown', 'Learning')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH005', 'Perfect Score', 'Achieve 100% on any quiz', 100, 'Score 100% on a quiz', 'star', 'Learning')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH006', 'Community Voice', 'Create your first discussion thread', 75, 'Create 1 thread', 'message-circle', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH007', 'Discussion Leader', 'Create 10 discussion threads', 250, 'Create 10 threads', 'megaphone', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH008', 'Popular Voice', 'Get 50 upvotes on a single thread', 200, 'Get 50 upvotes on 1 thread', 'trending-up', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH009', 'Helpful Member', 'Reply to 25 discussion threads', 150, 'Reply to 25 threads', 'users', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH010', 'Event Enthusiast', 'RSVP to your first event', 50, 'RSVP to 1 event', 'calendar', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH011', 'Social Butterfly', 'RSVP to 10 events', 300, 'RSVP to 10 events', 'calendar-check', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH012', 'Resource Contributor', 'Upload your first resource', 100, 'Upload 1 resource', 'upload', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH013', 'Knowledge Sharer', 'Upload 10 resources', 400, 'Upload 10 resources', 'folder-open', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH014', 'Resource Hunter', 'Download 25 resources', 200, 'Download 25 resources', 'download', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH015', 'Point Collector', 'Earn 500 total points', 100, 'Earn 500 points', 'coins', 'Rewards')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH016', 'Point Master', 'Earn 2500 total points', 250, 'Earn 2500 points', 'gem', 'Rewards')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH017', 'Badge Collector', 'Earn your first badge', 75, 'Earn 1 badge', 'award', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH018', 'Badge Enthusiast', 'Earn 5 different badges', 200, 'Earn 5 badges', 'shield', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH019', 'Early Bird', 'Log in 7 days in a row', 150, 'Login for 7 consecutive days', 'sunrise', 'Engagement')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH020', 'Dedicated Learner', 'Log in 30 days in a row', 500, 'Login for 30 consecutive days', 'zap', 'Engagement')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;

-- 6) Community threads
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500001, 'Discussions', 'Best practices for learning complex topics''', 'I am struggling with the advanced machine learning modules. What strategies do you use when tackling really complex technical topics'' Any tips on breaking down difficult concepts''', 'Sarah Chen', 1200, 'I am struggling with the advanced machine learning modules. What strategies do you use when tackling really complex tech', 'learning,tips,machine-learning', 12, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500002, 'Discussions', 'How to balance learning with full-time work''', 'I am working full-time and trying to complete modules. How do you all manage your time'' What is a realistic schedule for completing 1-2 modules per week''', 'Michael Torres', 1200, 'I am working full-time and trying to complete modules. How do you all manage your time'' What is a realistic schedule for', 'time-management,career,balance', 28, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500003, 'Discussions', 'Study group for Data Science track''', 'Looking to form a study group for the Data Science & Analytics modules. Anyone interested in meeting weekly to discuss concepts and work through quizzes together''', 'Priya Patel', 1200, 'Looking to form a study group for the Data Science & Analytics modules. Anyone interested in meeting weekly to discuss c', 'data-science,study-group,collaboration', 15, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500004, 'Discussions', 'Real-world application of Product Management concepts', 'I just finished the Product Strategy module. Has anyone applied the RICE framework or North Star Metric at their company'' Would love to hear real examples!', 'James Williams', 1200, 'I just finished the Product Strategy module. Has anyone applied the RICE framework or North Star Metric at their company', 'product-management,real-world,case-study', 22, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500005, 'Discussions', 'Recommended resources beyond the platform''', 'The Leadership modules are great! What books, podcasts, or blogs do you recommend to supplement the learning here'' Building my reading list.', 'Emily Rodriguez', 1200, 'The Leadership modules are great! What books, podcasts, or blogs do you recommend to supplement the learning here'' Build', 'resources,leadership,recommendations', 19, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500006, 'Discussions', 'Struggling with quiz on Statistical Analysis', 'The p-value and hypothesis testing questions are confusing me. Can someone explain the Central Limit Theorem in simpler terms'' The quiz explanations helped but I need more clarity.', 'David Kim', 1200, 'The p-value and hypothesis testing questions are confusing me. Can someone explain the Central Limit Theorem in simpler ', 'statistics,help,data-science', 8, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500007, 'Discussions', 'Career transition: Marketing to Product Management''', 'I am a digital marketer interested in transitioning to product management. Anyone made a similar career switch'' Which modules would you recommend I focus on''', 'Alexandra Brown', 1200, 'I am a digital marketer interested in transitioning to product management. Anyone made a similar career switch'' Which mo', 'career-change,product-management,advice', 31, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500008, 'Discussions', 'Share your MVP success stories!', 'Just completed the Product Fundamentals module about MVPs. Has anyone here successfully launched a minimum viable product'' What did you learn from the experience''', 'Robert Lee', 1200, 'Just completed the Product Fundamentals module about MVPs. Has anyone here successfully launched a minimum viable produc', 'mvp,entrepreneurship,product', 14, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500009, 'Discussions', 'Digital Marketing certifications worth it''', 'After completing the Digital Marketing modules here, thinking about getting certified. Are Google Ads or HubSpot certifications worth the investment'' Do they actually help with job prospects''', 'Jennifer Martinez', 1200, 'After completing the Digital Marketing modules here, thinking about getting certified. Are Google Ads or HubSpot certifi', 'certifications,digital-marketing,career', 25, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500010, 'Discussions', 'How to retain information better''', 'I complete the modules but feel like I forget everything after a week. What techniques do you use for better retention'' Do you take notes, create flashcards, or something else''', 'Thomas Anderson', 1200, 'I complete the modules but feel like I forget everything after a week. What techniques do you use for better retention'' ', 'learning,retention,study-tips', 17, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500011, 'Discussions', 'Python vs JavaScript for beginners''', 'The Technology Fundamentals track mentions both languages. For someone completely new to programming, which would you recommend starting with and why''', 'Lisa Wang', 1200, 'The Technology Fundamentals track mentions both languages. For someone completely new to programming, which would you re', 'programming,beginners,technology', 11, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500012, 'Discussions', 'Leadership lessons from remote work era', 'The leadership modules do not specifically address remote team management. What leadership challenges have you faced with distributed teams'' How did you adapt''', 'Marcus Johnson', 1200, 'The leadership modules do not specifically address remote team management. What leadership challenges have you faced wit', 'leadership,remote-work,management', 20, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500013, 'Discussions', 'Networking opportunities for learners''', 'Would anyone be interested in virtual coffee chats to discuss what we are learning'' I think it would be valuable to connect with other motivated learners!', 'Sophia Nguyen', 1200, 'Would anyone be interested in virtual coffee chats to discuss what we are learning'' I think it would be valuable to conn', 'networking,community,social', 33, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500014, 'Discussions', 'Data visualization tools - what do you use''', 'Completed the Data Science modules and want to practice visualizations. Do you prefer Tableau, Power BI, or Python libraries like Matplotlib'' What is easiest for beginners''', 'Christopher Davis', 1200, 'Completed the Data Science modules and want to practice visualizations. Do you prefer Tableau, Power BI, or Python libra', 'data-visualization,tools,data-science', 16, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500015, 'Discussions', 'Feedback on my product roadmap''', 'I created a product roadmap for a side project using concepts from the Product Management track. Happy to share it - would anyone be willing to review and provide feedback''', 'Amanda Garcia', 1200, 'I created a product roadmap for a side project using concepts from the Product Management track. Happy to share it - wou', 'product-management,feedback,project', 9, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500016, 'Discussions', 'How to explain technical concepts to non-technical people''', 'As I learn more technical topics, I struggle to explain them to my non-technical colleagues. Any tips on translating complex concepts for different audiences''', 'Kevin Zhang', 1200, 'As I learn more technical topics, I struggle to explain them to my non-technical colleagues. Any tips on translating com', 'communication,technical,soft-skills', 24, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500017, 'Discussions', 'Best module to start with as absolute beginner''', 'I am new to all these topics - tech, data, marketing, everything! Which learning path would you recommend for someone starting from zero'' Where did you begin''', 'Rachel Thompson', 1200, 'I am new to all these topics - tech, data, marketing, everything! Which learning path would you recommend for someone st', 'beginners,advice,getting-started', 13, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500018, 'Discussions', 'Success story: Got promoted after completing modules!', 'Just wanted to share - I applied concepts from the Leadership & Strategy track in my role and got promoted to team lead! The emotional intelligence and delegation modules were game-changers.', 'Daniel Park', 1200, 'Just wanted to share - I applied concepts from the Leadership & Strategy track in my role and got promoted to team lead!', 'success,career,leadership', 45, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500019, 'Discussions', 'Creating a personal learning schedule', 'Trying to create a structured 90-day learning plan. Has anyone mapped out their learning journey'' Would love to see examples of how you organized your path through the modules.', 'Olivia Martinez', 1200, 'Trying to create a structured 90-day learning plan. Has anyone mapped out their learning journey'' Would love to see exam', 'planning,organization,strategy', 18, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;
insert into public.community_threads (id, category, title, content, author, author_reputation, excerpt, tags, upvotes, view_count, created_at)
values (500020, 'Discussions', 'Quiz difficulty - is Advanced level worth it''', 'I have completed all Beginner and Intermediate modules. Are the Advanced modules significantly harder'' Do you feel the extra difficulty provides better learning outcomes''', 'Nathan Wilson', 1200, 'I have completed all Beginner and Intermediate modules. Are the Advanced modules significantly harder'' Do you feel the e', 'difficulty,advanced,learning-path', 21, 0, now())
on conflict (id) do update set
  category = excluded.category,
  title = excluded.title,
  content = excluded.content,
  author = excluded.author,
  excerpt = excluded.excerpt,
  tags = excluded.tags,
  upvotes = excluded.upvotes;

-- 7) grants for new catalog table
grant select, insert, update, delete on table public.reward_achievement_catalog to anon, authenticated;

-- 8) sequence resets
select setval(pg_get_serial_sequence('public.learning_topics','id'), (select coalesce(max(id),1) from public.learning_topics));
select setval(pg_get_serial_sequence('public.learning_modules','id'), (select coalesce(max(id),1) from public.learning_modules));
select setval(pg_get_serial_sequence('public.learning_sections','id'), (select coalesce(max(id),1) from public.learning_sections));
select setval(pg_get_serial_sequence('public.learning_quiz_questions','id'), (select coalesce(max(id),1) from public.learning_quiz_questions));
select setval(pg_get_serial_sequence('public.learning_quiz_options','id'), (select coalesce(max(id),1) from public.learning_quiz_options));
select setval(pg_get_serial_sequence('public.community_threads','id'), (select coalesce(max(id),1) from public.community_threads));

commit;

-- ===== END: platform_content_import.sql =====

-- ===== BEGIN: platform_content_final_import.sql =====
-- ============================================================
-- Platform Content Final Import (Achievements/Events/Resources/Badges)
-- Source: platform_content_final.xlsx
-- ============================================================

begin;

-- 1) Achievement catalog
create table if not exists public.reward_achievement_catalog (
  achievement_id text primary key,
  name text not null,
  description text not null,
  points int not null default 0,
  unlock_criteria text,
  icon_name text,
  category text,
  created_at timestamptz not null default now()
);
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH001', 'First Steps', 'Complete your first learning module', 50, 'Complete 1 module', 'trophy', 'Learning')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH002', 'Knowledge Seeker', 'Complete 5 learning modules', 150, 'Complete 5 modules', 'book', 'Learning')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH003', 'Scholar', 'Complete 15 learning modules', 500, 'Complete 15 modules', 'graduation-cap', 'Learning')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH004', 'Master Learner', 'Pass all modules across all topics', 1000, 'Pass all modules', 'crown', 'Learning')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH005', 'Perfect Score', 'Achieve 100% on any quiz', 100, 'Score 100% on a quiz', 'star', 'Learning')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH006', 'Community Voice', 'Create your first discussion thread', 75, 'Create 1 thread', 'message-circle', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH007', 'Discussion Leader', 'Create 10 discussion threads', 250, 'Create 10 threads', 'megaphone', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH008', 'Popular Voice', 'Get 50 upvotes on a single thread', 200, 'Get 50 upvotes on 1 thread', 'trending-up', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH009', 'Helpful Member', 'Reply to 25 discussion threads', 150, 'Reply to 25 threads', 'users', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH010', 'Event Enthusiast', 'RSVP to your first event', 50, 'RSVP to 1 event', 'calendar', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH011', 'Social Butterfly', 'RSVP to 10 events', 300, 'RSVP to 10 events', 'calendar-check', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH012', 'Resource Contributor', 'Upload your first resource', 100, 'Upload 1 resource', 'upload', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH013', 'Knowledge Sharer', 'Upload 10 resources', 400, 'Upload 10 resources', 'folder-open', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH014', 'Resource Hunter', 'Download 25 resources', 200, 'Download 25 resources', 'download', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH015', 'Point Collector', 'Earn 500 total points', 100, 'Earn 500 points', 'coins', 'Rewards')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH016', 'Point Master', 'Earn 2500 total points', 250, 'Earn 2500 points', 'gem', 'Rewards')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH017', 'Badge Collector', 'Earn your first badge', 75, 'Earn 1 badge', 'award', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH018', 'Badge Enthusiast', 'Earn 5 different badges', 200, 'Earn 5 badges', 'shield', 'Community')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH019', 'Early Bird', 'Log in 7 days in a row', 150, 'Login for 7 consecutive days', 'sunrise', 'Engagement')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;
insert into public.reward_achievement_catalog (achievement_id, name, description, points, unlock_criteria, icon_name, category)
values ('ACH020', 'Dedicated Learner', 'Log in 30 days in a row', 500, 'Login for 30 consecutive days', 'zap', 'Engagement')
on conflict (achievement_id) do update set
  name = excluded.name,
  description = excluded.description,
  points = excluded.points,
  unlock_criteria = excluded.unlock_criteria,
  icon_name = excluded.icon_name,
  category = excluded.category;

-- 2) Events content into community_events
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650001, 'Introduction to Python Programming', 'WORKSHOP', 'Workshop', 'Dr. Lisa Zhang', 'Community Organizer', '2026-02-20 18:00:00+08', 'SGT', true, 'Virtual - Zoom', 0, 50, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650002, 'Data Visualization Best Practices', 'WORKSHOP', 'Workshop', 'Michael Chen', 'Community Organizer', '2026-02-22 14:00:00+08', 'SGT', true, 'Virtual - Teams', 0, 40, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650003, 'Product Management AMA with Sarah Johnson', 'AMA', 'AMA', 'Sarah Johnson', 'Community Organizer', '2026-02-25 19:00:00+08', 'SGT', true, 'Virtual - Zoom', 0, 100, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650004, 'SEO & Content Marketing Masterclass', 'MASTERCLASS', 'Masterclass', 'Emma Rodriguez', 'Community Organizer', '2026-02-27 17:00:00+08', 'SGT', true, 'Virtual - Google Meet', 0, 60, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650005, 'Building Your First Machine Learning Model', 'WORKSHOP', 'Workshop', 'Dr. Raj Patel', 'Community Organizer', '2026-03-01 15:00:00+08', 'SGT', true, 'Virtual - Zoom', 0, 35, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650006, 'Leadership in Tech: Panel Discussion', 'PANEL', 'Panel', 'Marcus Williams', 'Community Organizer', '2026-03-03 18:30:00+08', 'SGT', true, 'Virtual - Zoom', 0, 75, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650007, 'A/B Testing for Product Managers', 'WORKSHOP', 'Workshop', 'Jennifer Park', 'Community Organizer', '2026-03-05 16:00:00+08', 'SGT', true, 'Virtual - Teams', 0, 45, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650008, 'Networking Social Hour', 'SOCIAL', 'Social', 'Community Team', 'Community Organizer', '2026-03-06 19:00:00+08', 'SGT', true, 'Virtual - Gather.town', 0, 80, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650009, 'Advanced SQL for Data Analysis', 'WORKSHOP', 'Workshop', 'David Kim', 'Community Organizer', '2026-03-08 14:00:00+08', 'SGT', true, 'Virtual - Zoom', 0, 40, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650010, 'Personal Branding for Career Growth', 'WORKSHOP', 'Workshop', 'Amanda Taylor', 'Community Organizer', '2026-03-10 18:00:00+08', 'SGT', true, 'Virtual - Zoom', 0, 55, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650011, 'Agile Methodologies Deep Dive', 'WORKSHOP', 'Workshop', 'Robert Chen', 'Community Organizer', '2026-03-12 17:00:00+08', 'SGT', true, 'Virtual - Teams', 0, 50, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650012, 'Data Ethics and Privacy', 'DISCUSSION', 'Discussion', 'Dr. Sophia Martinez', 'Community Organizer', '2026-03-13 19:00:00+08', 'SGT', true, 'Virtual - Zoom', 0, 60, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650013, 'Google Analytics 4 Essentials', 'WORKSHOP', 'Workshop', 'Kevin O''Brien', 'Community Organizer', '2026-03-15 15:00:00+08', 'SGT', true, 'Virtual - Google Meet', 0, 45, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650014, 'Career Transitions: From Engineer to PM', 'PANEL', 'Panel', 'Multiple Speakers', 'Community Organizer', '2026-03-17 18:30:00+08', 'SGT', true, 'Virtual - Zoom', 0, 70, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650015, 'UI/UX Design Fundamentals', 'WORKSHOP', 'Workshop', 'Jessica Wong', 'Community Organizer', '2026-03-19 16:00:00+08', 'SGT', true, 'Virtual - Zoom', 0, 50, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650016, 'Building Dashboards with Power BI', 'WORKSHOP', 'Workshop', 'Thomas Anderson', 'Community Organizer', '2026-03-20 14:00:00+08', 'SGT', true, 'Virtual - Teams', 0, 40, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650017, 'Study Group: Data Science Track', 'STUDY GROUP', 'Study Group', 'Community Members', 'Community Organizer', '2026-03-21 19:00:00+08', 'SGT', true, 'Virtual - Zoom', 0, 30, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650018, 'Email Marketing Automation', 'WORKSHOP', 'Workshop', 'Rachel Green', 'Community Organizer', '2026-03-22 17:00:00+08', 'SGT', true, 'Virtual - Zoom', 0, 50, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650019, 'Kubernetes for Beginners', 'WORKSHOP', 'Workshop', 'Chris Taylor', 'Community Organizer', '2026-03-24 18:00:00+08', 'SGT', true, 'Virtual - Zoom', 0, 35, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;
insert into public.community_events
  (id, title, event_type, category, host_name, host_title, start_at, timezone, is_online, location, seats_booked, seats_total, status_label, is_registered, reminder_set, action_label)
values (650020, 'Monthly Demo Day', 'SHOWCASE', 'Showcase', 'Community Team', 'Community Organizer', '2026-03-26 19:00:00+08', 'SGT', true, 'Virtual - Zoom', 0, 100, null, false, false, 'RSVP')
on conflict (id) do update set
  title = excluded.title,
  event_type = excluded.event_type,
  category = excluded.category,
  host_name = excluded.host_name,
  host_title = excluded.host_title,
  start_at = excluded.start_at,
  timezone = excluded.timezone,
  is_online = excluded.is_online,
  location = excluded.location,
  seats_total = excluded.seats_total;

-- 3) Resources content into community_resources
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660001, 'Python Cheat Sheet for Beginners', 'Lisa Zhang', 'Comprehensive one-page reference for Python syntax, data structures, and common functions. Perfect for quick lookups while coding.', 'python', 'cheatsheet', 'PDF', null, null, 'https://example.com/resources/res001', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660002, 'Data Visualization Color Palette Guide', 'Michael Chen', 'Professional color palettes for data visualization. Includes accessible color combinations and usage guidelines for different chart types.', 'visualization', 'design', 'PDF', null, null, 'https://example.com/resources/res002', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660003, 'Product Roadmap Template (Figma)', 'Sarah Johnson', 'Ready-to-use product roadmap template in Figma. Includes quarterly and annual views with customizable components.', 'roadmap', 'template', 'Link', null, null, 'https://example.com/resources/res003', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660004, 'SEO Checklist for Content Writers', 'Emma Rodriguez', 'Complete checklist covering keyword research, on-page SEO, meta tags, and content optimization strategies.', 'seo', 'checklist', 'PDF', null, null, 'https://example.com/resources/res004', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660005, 'Machine Learning Algorithms Comparison', 'Raj Patel', 'Visual guide comparing common ML algorithms, their use cases, pros/cons, and when to use each one.', 'machine-learning', 'algorithms', 'PDF', null, null, 'https://example.com/resources/res005', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660006, 'Remote Team Leadership Playbook', 'Marcus Williams', '30-page guide on managing distributed teams effectively. Covers communication, culture, and productivity strategies.', 'remote-work', 'leadership', 'PDF', null, null, 'https://example.com/resources/res006', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660007, 'A/B Testing Calculator Spreadsheet', 'Jennifer Park', 'Excel template for calculating statistical significance in A/B tests. Includes examples and documentation.', 'testing', 'statistics', 'Excel', null, null, 'https://example.com/resources/res007', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660008, 'SQL Query Template Library', 'David Kim', 'Collection of 50+ reusable SQL query templates for common data analysis tasks. Includes window functions and CTEs.', 'sql', 'queries', 'PDF', null, null, 'https://example.com/resources/res008', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660009, 'LinkedIn Profile Optimization Guide', 'Amanda Taylor', 'Step-by-step guide to creating a standout LinkedIn profile. Includes headline formulas and summary templates.', 'linkedin', 'career', 'PDF', null, null, 'https://example.com/resources/res009', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660010, 'User Story Template Pack', 'Robert Chen', 'Set of user story templates following best practices. Includes acceptance criteria and estimation guides.', 'user-stories', 'agile', 'PDF', null, null, 'https://example.com/resources/res010', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660011, 'Data Privacy Compliance Checklist', 'Sophia Martinez', 'GDPR and data privacy compliance checklist for data professionals. Covers collection, storage, and processing.', 'privacy', 'gdpr', 'PDF', null, null, 'https://example.com/resources/res011', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660012, 'Google Analytics 4 Setup Guide', 'Kevin O''Brien', 'Complete guide to setting up GA4, including event tracking, custom dimensions, and migration from Universal Analytics.', 'analytics', 'google', 'PDF', null, null, 'https://example.com/resources/res012', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660013, 'UX Design Process Flowchart', 'Jessica Wong', 'Visual flowchart of the complete UX design process from research to testing. Includes deliverables at each stage.', 'ux', 'design', 'PDF', null, null, 'https://example.com/resources/res013', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660014, 'Power BI DAX Formula Reference', 'Thomas Anderson', 'Quick reference guide for common DAX formulas in Power BI. Includes examples and use cases for each formula.', 'power-bi', 'dax', 'PDF', null, null, 'https://example.com/resources/res014', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660015, 'Email Marketing Copy Templates', 'Rachel Green', '20 proven email templates for different campaign types. Includes subject lines and call-to-action suggestions.', 'email', 'templates', 'PDF', null, null, 'https://example.com/resources/res015', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660016, 'Docker Commands Cheat Sheet', 'Chris Taylor', 'Essential Docker commands for container management. Covers images, containers, networks, and volumes.', 'docker', 'containers', 'PDF', null, null, 'https://example.com/resources/res016', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660017, 'Product Metrics Dashboard Template', 'Sarah Johnson', 'Comprehensive metrics dashboard template tracking user engagement, retention, and growth. Includes calculation formulas.', 'metrics', 'dashboard', 'Excel', null, null, 'https://example.com/resources/res017', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660018, 'Presentation Design Guide', 'Amanda Taylor', 'Best practices for creating impactful presentations. Covers layout, typography, color theory, and storytelling.', 'presentations', 'design', 'PDF', null, null, 'https://example.com/resources/res018', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660019, 'API Documentation Template', 'Lisa Zhang', 'Professional API documentation template with examples. Includes endpoint descriptions, parameters, and response formats.', 'api', 'documentation', 'PDF', null, null, 'https://example.com/resources/res019', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660020, 'Career Development Framework', 'Marcus Williams', 'Structured framework for planning your career progression. Includes skill mapping and goal-setting worksheets.', 'career', 'development', 'PDF', null, null, 'https://example.com/resources/res020', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660021, 'Statistics for Data Science Handbook', 'Raj Patel', '50-page handbook covering essential statistical concepts for data scientists. Includes examples and R code snippets.', 'statistics', 'handbook', 'PDF', null, null, 'https://example.com/resources/res021', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660022, 'Content Calendar Template', 'Emma Rodriguez', 'Editorial calendar template for planning content across multiple channels. Includes campaign tracking and analytics.', 'content', 'calendar', 'Excel', null, null, 'https://example.com/resources/res022', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660023, 'Git Workflow Diagram', 'David Kim', 'Visual guide to Git branching strategies and workflows. Covers feature branches, releases, and hotfixes.', 'git', 'version-control', 'PDF', null, null, 'https://example.com/resources/res023', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660024, 'Customer Journey Map Template', 'Jennifer Park', 'Customizable customer journey mapping template. Includes touchpoints, emotions, and pain points sections.', 'customer-journey', 'template', 'PDF', null, null, 'https://example.com/resources/res024', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;
insert into public.community_resources
  (id, title, author, summary, tag_primary, tag_secondary, file_type, file_name, file_path, file_url, file_size, download_count, points_reward)
values (660025, 'Accessibility Guidelines Checklist', 'Jessica Wong', 'Web accessibility checklist following WCAG 2.1 standards. Covers screen readers, keyboard navigation, and color contrast.', 'accessibility', 'wcag', 'PDF', null, null, 'https://example.com/resources/res025', 0, 0, 5)
on conflict (id) do update set
  title = excluded.title,
  author = excluded.author,
  summary = excluded.summary,
  tag_primary = excluded.tag_primary,
  tag_secondary = excluded.tag_secondary,
  file_type = excluded.file_type,
  file_url = excluded.file_url,
  points_reward = excluded.points_reward;

-- 4) Badge catalog
create table if not exists public.community_badge_catalog (
  badge_id text primary key,
  name text not null,
  description text not null,
  icon_name text,
  requirements text,
  category text,
  tier text,
  points_reward int not null default 0,
  created_at timestamptz not null default now()
);
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG001', 'Quick Learner', 'Complete 3 modules in one week', 'zap', 'Complete 3 modules within 7 days', 'Learning', 'Bronze', 100)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG002', 'Night Owl', 'Complete a quiz after 10 PM', 'moon', 'Complete any quiz between 10 PM and 6 AM', 'Engagement', 'Bronze', 50)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG003', 'Perfect Start', 'Score 100% on your first quiz attempt', 'target', 'Get 100% on first quiz of any module', 'Learning', 'Silver', 150)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG004', 'Discussion Starter', 'Create a thread that gets 20+ upvotes', 'message-square', 'Create a thread that receives 20 or more upvotes', 'Community', 'Silver', 200)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG005', 'Helpful Hand', 'Reply to 10 different discussion threads', 'hand-helping', 'Post replies to 10 unique threads', 'Community', 'Bronze', 75)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG006', 'Data Explorer', 'Complete all Data Science modules', 'bar-chart-2', 'Pass all modules in Data Science & Analytics topic', 'Learning', 'Gold', 500)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG007', 'Tech Wizard', 'Complete all Technology Fundamentals modules', 'cpu', 'Pass all modules in Technology Fundamentals topic', 'Learning', 'Gold', 500)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG008', 'Marketing Guru', 'Complete all Digital Marketing modules', 'trending-up', 'Pass all modules in Digital Marketing topic', 'Learning', 'Gold', 500)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG009', 'Product Pro', 'Complete all Product Management modules', 'package', 'Pass all modules in Product Management topic', 'Learning', 'Gold', 500)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG010', 'Strategic Leader', 'Complete all Leadership & Strategy modules', 'compass', 'Pass all modules in Leadership & Strategy topic', 'Learning', 'Gold', 500)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG011', 'Resource Collector', 'Download 15 different resources', 'folder-down', 'Download 15 unique resources from the library', 'Community', 'Bronze', 100)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG012', 'Event Regular', 'Attend 5 community events', 'calendar-check', 'RSVP and participate in 5 events', 'Community', 'Silver', 250)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG013', 'Knowledge Sharer', 'Upload 5 resources that get downloaded 10+ times each', 'share-2', 'Upload 5 resources with 10+ downloads each', 'Community', 'Gold', 400)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG014', 'Streak Master', 'Maintain a 14-day learning streak', 'flame', 'Log in and complete learning activities for 14 consecutive days', 'Engagement', 'Silver', 300)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG015', 'Community Champion', 'Earn 1000 reputation points', 'star', 'Accumulate 1000 total reputation points from community activities', 'Community', 'Gold', 500)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG016', 'Quiz Master', 'Score 90% or higher on 10 different quizzes', 'award', 'Achieve 90% or better on 10 unique module quizzes', 'Learning', 'Silver', 250)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG017', 'Early Adopter', 'Join within the first month of platform launch', 'rocket', 'Create account within first 30 days of launch', 'Engagement', 'Bronze', 100)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG018', 'Conversation Catalyst', 'Start 5 threads that each get 5+ replies', 'message-circle-more', 'Create 5 threads with 5 or more replies each', 'Community', 'Silver', 200)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG019', 'Renaissance Learner', 'Complete at least one module from each topic', 'book-open', 'Complete 1 module from all 5 learning topics', 'Learning', 'Silver', 300)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG020', 'Peak Performer', 'Score 100% on an Advanced level quiz', 'trophy', 'Achieve perfect score on any Advanced difficulty quiz', 'Learning', 'Gold', 350)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG021', 'Social Connector', 'Get 50 total upvotes across all your threads', 'users', 'Accumulate 50 upvotes across all threads you created', 'Community', 'Silver', 200)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG022', 'Feedback Champion', 'Provide detailed feedback on 5 community threads', 'clipboard-check', 'Write substantive replies (100+ words) on 5 different threads', 'Community', 'Bronze', 150)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG023', 'Weekend Warrior', 'Complete 5 modules on weekends', 'sun', 'Complete 5 modules on Saturday or Sunday', 'Engagement', 'Bronze', 100)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG024', 'Point Millionaire', 'Earn 5000 total points', 'gem', 'Accumulate 5000 lifetime points from all activities', 'Rewards', 'Gold', 500)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;
insert into public.community_badge_catalog (badge_id, name, description, icon_name, requirements, category, tier, points_reward)
values ('BDG025', 'Unstoppable', 'Complete 30 modules total', 'infinity', 'Successfully pass 30 modules across all topics', 'Learning', 'Gold', 750)
on conflict (badge_id) do update set
  name = excluded.name,
  description = excluded.description,
  icon_name = excluded.icon_name,
  requirements = excluded.requirements,
  category = excluded.category,
  tier = excluded.tier,
  points_reward = excluded.points_reward;

-- 5) grants for new catalog tables
grant select, insert, update, delete on table public.reward_achievement_catalog to anon, authenticated;
grant select, insert, update, delete on table public.community_badge_catalog to anon, authenticated;

-- 6) materialize catalog badges into user badge table (for Admin UI)
insert into public.community_badges
  (id, user_id, title, description, status, points, earned_at, progress_current, progress_target, progress_percent, icon, icon_color, created_at)
select
  800000 + row_number() over (order by c.badge_id) as id,
  'Admin' as user_id,
  c.name as title,
  c.description,
  'in_progress' as status,
  c.points_reward as points,
  null::date as earned_at,
  0 as progress_current,
  1 as progress_target,
  0 as progress_percent,
  'bi-award' as icon,
  'text-warning' as icon_color,
  now() as created_at
from public.community_badge_catalog c
where not exists (
  select 1
  from public.community_badges b
  where b.user_id = 'Admin'
    and lower(b.title) = lower(c.name)
);

update public.community_profiles p
set badges_earned_count = coalesce((
  select count(*)
  from public.community_badges b
  where b.user_id = p.user_id
    and b.status = 'earned'
), 0),
updated_at = now()
where p.user_id = 'Admin';

-- 7) sequence resets for touched serial tables
select setval(pg_get_serial_sequence('public.community_events','id'), (select coalesce(max(id),1) from public.community_events));
select setval(pg_get_serial_sequence('public.community_resources','id'), (select coalesce(max(id),1) from public.community_resources));
select setval(pg_get_serial_sequence('public.community_badges','id'), (select coalesce(max(id),1) from public.community_badges));
select setval(pg_get_serial_sequence('public.community_thread_replies','id'), (select coalesce(max(id),1) from public.community_thread_replies));
select setval(pg_get_serial_sequence('public.community_thread_votes','id'), (select coalesce(max(id),1) from public.community_thread_votes));
select setval(pg_get_serial_sequence('public.community_thread_views','id'), (select coalesce(max(id),1) from public.community_thread_views));
select setval(pg_get_serial_sequence('public.community_resource_downloads','id'), (select coalesce(max(id),1) from public.community_resource_downloads));
select setval(pg_get_serial_sequence('public.community_event_rsvps','id'), (select coalesce(max(id),1) from public.community_event_rsvps));

commit;

-- ===== END: platform_content_final_import.sql =====



-- ============================================================
-- MEDIA IMPORT (merged from platform_content_media_import.sql)
-- ============================================================

-- Generated from platform_content_media.xlsx
-- Note: workbook contains image URLs for Events/Resources/Achievements/Badges only (no learning topic video URLs).

-- ============================================================
-- 1) Reward Achievements (image_url)
-- ============================================================
alter table public.reward_achievement_catalog add column if not exists image_url text;

with m(achievement_id, image_url) as (
values
('ACH001','https://images.unsplash.com/photo-1567427017947-545c5f8d16ad?w=800'),
('ACH002','https://images.unsplash.com/photo-1481627834876-b7833e8f5570?w=800'),
('ACH003','https://images.unsplash.com/photo-1523050854058-8df90110c9f1?w=800'),
('ACH004','https://images.unsplash.com/photo-1607827448387-a67db1383b59?w=800'),
('ACH005','https://images.unsplash.com/photo-1604480133435-25b75a0e6736?w=800'),
('ACH006','https://images.unsplash.com/photo-1557804506-669a67965ba0?w=800'),
('ACH007','https://images.unsplash.com/photo-1552664730-d307ca884978?w=800'),
('ACH008','https://images.unsplash.com/photo-1460925895917-afdab827c52f?w=800'),
('ACH009','https://images.unsplash.com/photo-1522071820081-009f0129c71c?w=800'),
('ACH010','https://images.unsplash.com/photo-1506784983877-45594efa4cbe?w=800'),
('ACH011','https://images.unsplash.com/photo-1511578314322-379afb476865?w=800'),
('ACH012','https://images.unsplash.com/photo-1553484771-8541a4e1e0e9?w=800'),
('ACH013','https://images.unsplash.com/photo-1450101499163-c8848c66ca85?w=800'),
('ACH014','https://images.unsplash.com/photo-1521737711867-e3b97375f902?w=800'),
('ACH015','https://images.unsplash.com/photo-1621761191319-c6fb62004040?w=800'),
('ACH016','https://images.unsplash.com/photo-1620321023374-d1a68fbc720d?w=800'),
('ACH017','https://images.unsplash.com/photo-1567427017947-545c5f8d16ad?w=800'),
('ACH018','https://images.unsplash.com/photo-1604480133435-25b75a0e6736?w=800'),
('ACH019','https://images.unsplash.com/photo-1495364141860-b0d03eccd065?w=800'),
('ACH020','https://images.unsplash.com/photo-1516796181074-bf453fbfa3e6?w=800')
)
update public.reward_achievement_catalog a
set image_url = m.image_url
from m
where a.achievement_id = m.achievement_id;

-- ============================================================
-- 2) Community Badge Catalog (image_url)
-- ============================================================
alter table public.community_badge_catalog add column if not exists image_url text;

with m(badge_id, image_url) as (
values
('BDG001','https://images.unsplash.com/photo-1516796181074-bf453fbfa3e6?w=800'),
('BDG002','https://images.unsplash.com/photo-1517483000871-1dbf64a6e1c6?w=800'),
('BDG003','https://images.unsplash.com/photo-1551836022-d5d88e9218df?w=800'),
('BDG004','https://images.unsplash.com/photo-1553484771-8541a4e1e0e9?w=800'),
('BDG005','https://images.unsplash.com/photo-1582213782179-e0d53f98f2ca?w=800'),
('BDG006','https://images.unsplash.com/photo-1551288049-bebda4e38f71?w=800'),
('BDG007','https://images.unsplash.com/photo-1518770660439-4636190af475?w=800'),
('BDG008','https://images.unsplash.com/photo-1460925895917-afdab827c52f?w=800'),
('BDG009','https://images.unsplash.com/photo-1553413077-190dd305871c?w=800'),
('BDG010','https://images.unsplash.com/photo-1504868584819-f8e8b4b6d7e3?w=800'),
('BDG011','https://images.unsplash.com/photo-1544716278-ca5e3f4abd8c?w=800'),
('BDG012','https://images.unsplash.com/photo-1506784983877-45594efa4cbe?w=800'),
('BDG013','https://images.unsplash.com/photo-1525338078858-d762b5e32f2c?w=800'),
('BDG014','https://images.unsplash.com/photo-1516796181074-bf453fbfa3e6?w=800'),
('BDG015','https://images.unsplash.com/photo-1604480133435-25b75a0e6736?w=800'),
('BDG016','https://images.unsplash.com/photo-1567427017947-545c5f8d16ad?w=800'),
('BDG017','https://images.unsplash.com/photo-1516849841032-87cbac4d88f7?w=800'),
('BDG018','https://images.unsplash.com/photo-1557804506-669a67965ba0?w=800'),
('BDG019','https://images.unsplash.com/photo-1481627834876-b7833e8f5570?w=800'),
('BDG020','https://images.unsplash.com/photo-1607827448387-a67db1383b59?w=800'),
('BDG021','https://images.unsplash.com/photo-1522071820081-009f0129c71c?w=800'),
('BDG022','https://images.unsplash.com/photo-1484480974693-6ca0a78fb36b?w=800'),
('BDG023','https://images.unsplash.com/photo-1495364141860-b0d03eccd065?w=800'),
('BDG024','https://images.unsplash.com/photo-1620321023374-d1a68fbc720d?w=800'),
('BDG025','https://images.unsplash.com/photo-1620121692029-d088224ddc74?w=800')
)
update public.community_badge_catalog b
set image_url = m.image_url
from m
where b.badge_id = m.badge_id;

-- ============================================================
-- 3) Community Events (image_url by title)
-- ============================================================
alter table public.community_events add column if not exists image_url text;

with m(title, image_url) as (
values
('Introduction to Python Programming','https://images.unsplash.com/photo-1526379095098-d400fd0bf935?w=800'),
('Data Visualization Best Practices','https://images.unsplash.com/photo-1551288049-bebda4e38f71?w=800'),
('Product Management AMA with Sarah Johnson','https://images.unsplash.com/photo-1552664730-d307ca884978?w=800'),
('SEO & Content Marketing Masterclass','https://images.unsplash.com/photo-1571721795195-a2ca3ef6d460?w=800'),
('Building Your First Machine Learning Model','https://images.unsplash.com/photo-1555949963-aa79dcee981c?w=800'),
('Leadership in Tech: Panel Discussion','https://images.unsplash.com/photo-1542744173-8e7e53415bb0?w=800'),
('A/B Testing for Product Managers','https://images.unsplash.com/photo-1460925895917-afdab827c52f?w=800'),
('Networking Social Hour','https://images.unsplash.com/photo-1511578314322-379afb476865?w=800'),
('Advanced SQL for Data Analysis','https://images.unsplash.com/photo-1544383835-bda2bc66a55d?w=800'),
('Personal Branding for Career Growth','https://images.unsplash.com/photo-1557804506-669a67965ba0?w=800'),
('Agile Methodologies Deep Dive','https://images.unsplash.com/photo-1454165804606-c3d57bc86b40?w=800'),
('Data Ethics and Privacy','https://images.unsplash.com/photo-1563986768494-4dee2763ff3f?w=800'),
('Google Analytics 4 Essentials','https://images.unsplash.com/photo-1551288049-bebda4e38f71?w=800'),
('Career Transitions: From Engineer to PM','https://images.unsplash.com/photo-1521737711867-e3b97375f902?w=800'),
('UI/UX Design Fundamentals','https://images.unsplash.com/photo-1581291518633-83b4ebd1d83e?w=800'),
('Building Dashboards with Power BI','https://images.unsplash.com/photo-1551288049-bebda4e38f71?w=800'),
('Study Group: Data Science Track','https://images.unsplash.com/photo-1522071820081-009f0129c71c?w=800'),
('Email Marketing Automation','https://images.unsplash.com/photo-1563986768494-4dee2763ff3f?w=800'),
('Kubernetes for Beginners','https://images.unsplash.com/photo-1667372393119-3d4c48d07fc9?w=800'),
('Monthly Demo Day','https://images.unsplash.com/photo-1559136555-9303baea8ebd?w=800')
)
update public.community_events e
set image_url = m.image_url
from m
where e.title = m.title;

-- ============================================================
-- 4) Community Resources (image_url by title)
-- ============================================================
alter table public.community_resources add column if not exists image_url text;

with m(title, image_url) as (
values
('Python Cheat Sheet for Beginners','https://images.unsplash.com/photo-1526379095098-d400fd0bf935?w=800'),
('Data Visualization Color Palette Guide','https://images.unsplash.com/photo-1551288049-bebda4e38f71?w=800'),
('Product Roadmap Template (Figma)','https://images.unsplash.com/photo-1454165804606-c3d57bc86b40?w=800'),
('SEO Checklist for Content Writers','https://images.unsplash.com/photo-1571721795195-a2ca3ef6d460?w=800'),
('Machine Learning Algorithms Comparison','https://images.unsplash.com/photo-1555949963-aa79dcee981c?w=800'),
('Remote Team Leadership Playbook','https://images.unsplash.com/photo-1600880292203-757bb62b4baf?w=800'),
('A/B Testing Calculator Spreadsheet','https://images.unsplash.com/photo-1551288049-bebda4e38f71?w=800'),
('SQL Query Template Library','https://images.unsplash.com/photo-1544383835-bda2bc66a55d?w=800'),
('LinkedIn Profile Optimization Guide','https://images.unsplash.com/photo-1611944212129-29977ae1398c?w=800'),
('User Story Template Pack','https://images.unsplash.com/photo-1484480974693-6ca0a78fb36b?w=800'),
('Data Privacy Compliance Checklist','https://images.unsplash.com/photo-1563986768494-4dee2763ff3f?w=800'),
('Google Analytics 4 Setup Guide','https://images.unsplash.com/photo-1551288049-bebda4e38f71?w=800'),
('UX Design Process Flowchart','https://images.unsplash.com/photo-1581291518633-83b4ebd1d83e?w=800'),
('Power BI DAX Formula Reference','https://images.unsplash.com/photo-1551288049-bebda4e38f71?w=800'),
('Email Marketing Copy Templates','https://images.unsplash.com/photo-1563986768494-4dee2763ff3f?w=800'),
('Docker Commands Cheat Sheet','https://images.unsplash.com/photo-1605745341112-85968b19335b?w=800'),
('Product Metrics Dashboard Template','https://images.unsplash.com/photo-1551288049-bebda4e38f71?w=800'),
('Presentation Design Guide','https://images.unsplash.com/photo-1559136555-9303baea8ebd?w=800'),
('API Documentation Template','https://images.unsplash.com/photo-1558494949-ef010cbdcc31?w=800'),
('Career Development Framework','https://images.unsplash.com/photo-1507679799987-c73779587ccf?w=800'),
('Statistics for Data Science Handbook','https://images.unsplash.com/photo-1551288049-bebda4e38f71?w=800'),
('Content Calendar Template','https://images.unsplash.com/photo-1484480974693-6ca0a78fb36b?w=800'),
('Git Workflow Diagram','https://images.unsplash.com/photo-1556075798-4825dfaaf498?w=800'),
('Customer Journey Map Template','https://images.unsplash.com/photo-1460925895917-afdab827c52f?w=800'),
('Accessibility Guidelines Checklist','https://images.unsplash.com/photo-1581291518633-83b4ebd1d83e?w=800')
)
update public.community_resources r
set image_url = m.image_url
from m
where r.title = m.title;

-- ============================================================
-- 5) Learning Hub media (normalized per section order)
-- ============================================================
alter table public.learning_sections add column if not exists image_url text;
alter table public.learning_sections add column if not exists video_url text;

-- Reset existing learning media first
update public.learning_sections
set image_url = null,
    video_url = null;

-- order 1 => primary image (unique per module level)
update public.learning_sections s
set image_url = case
  when m.topic_id = 1 and m.difficulty = 0 then 'https://images.unsplash.com/photo-1554224155-6726b3ff858f?w=1200&q=80&auto=format&fit=crop'
  when m.topic_id = 1 and m.difficulty = 1 then 'https://images.unsplash.com/photo-1450101499163-c8848c66ca85?w=1200&q=80&auto=format&fit=crop'
  when m.topic_id = 1 and m.difficulty = 2 then 'https://images.unsplash.com/photo-1526304640581-d334cdbbf45e?w=1200&q=80&auto=format&fit=crop'

  when m.topic_id = 2 and m.difficulty = 0 then 'https://images.unsplash.com/photo-1563013544-824ae1b704d3?w=1200&q=80&auto=format&fit=crop'
  when m.topic_id = 2 and m.difficulty = 1 then 'https://images.unsplash.com/photo-1556740749-887f6717d7e4?w=1200&q=80&auto=format&fit=crop'
  when m.topic_id = 2 and m.difficulty = 2 then 'https://images.unsplash.com/photo-1601597111158-2fceff292cdc?w=1200&q=80&auto=format&fit=crop'

  when m.topic_id = 3 and m.difficulty = 0 then 'https://images.unsplash.com/photo-1460925895917-afdab827c52f?w=1200&q=80&auto=format&fit=crop'
  when m.topic_id = 3 and m.difficulty = 1 then 'https://images.unsplash.com/photo-1526628953301-3e589a6a8b74?w=1200&q=80&auto=format&fit=crop'
  when m.topic_id = 3 and m.difficulty = 2 then 'https://images.unsplash.com/photo-1554224154-26032cdc32b8?w=1200&q=80&auto=format&fit=crop'

  when m.topic_id = 4 and m.difficulty = 0 then 'https://images.unsplash.com/photo-1521791136064-7986c2920216?w=1200&q=80&auto=format&fit=crop'
  when m.topic_id = 4 and m.difficulty = 1 then 'https://images.unsplash.com/photo-1519389950473-47ba0277781c?w=1200&q=80&auto=format&fit=crop'
  when m.topic_id = 4 and m.difficulty = 2 then 'https://images.unsplash.com/photo-1520607162513-77705c0f0d4a?w=1200&q=80&auto=format&fit=crop'

  when m.topic_id = 5 and m.difficulty = 0 then 'https://images.unsplash.com/photo-1553484771-cc0d9b8c2b33?w=1200&q=80&auto=format&fit=crop'
  when m.topic_id = 5 and m.difficulty = 1 then 'https://images.unsplash.com/photo-1521737604893-d14cc237f11d?w=1200&q=80&auto=format&fit=crop'
  when m.topic_id = 5 and m.difficulty = 2 then 'https://images.unsplash.com/photo-1559526324-4b87b5e36e44?w=1200&q=80&auto=format&fit=crop'
  else null
end
from public.learning_modules m
join public.learning_topics t on t.id = m.topic_id
where s.module_id = m.id
  and s."order" = 1;

-- order 2 => topic-related YouTube videos (Beginner/Intermediate)
update public.learning_sections s
set video_url = case
  when m.topic_id = 1 and m.difficulty = 0 then 'https://www.youtube.com/embed/-bqeNE1DOzA'
  when m.topic_id = 1 and m.difficulty = 1 then 'https://www.youtube.com/embed/Ryn49zHaYcM'
  when m.topic_id = 1 and m.difficulty = 2 then 'https://www.youtube.com/embed/Izw-xaVkO0g'

  -- swapped to a commonly embeddable Khan Academy video for beginner credit
  when m.topic_id = 2 and m.difficulty = 0 then 'https://www.youtube.com/embed/YoOsvcxLy40'
  when m.topic_id = 2 and m.difficulty = 1 then 'https://www.youtube.com/embed/MqqXTrEEZ7Y'
  when m.topic_id = 2 and m.difficulty = 2 then 'https://www.youtube.com/embed/8AtM1R9NmwM'

  when m.topic_id = 3 and m.difficulty = 0 then 'https://www.youtube.com/embed/9kKlZQGEOto'
  when m.topic_id = 3 and m.difficulty = 1 then 'https://www.youtube.com/embed/4SNWA_HbF6U'
  when m.topic_id = 3 and m.difficulty = 2 then 'https://www.youtube.com/embed/XvHAlui-Bno'

  when m.topic_id = 4 and m.difficulty = 0 then 'https://www.youtube.com/embed/yMg3gJx48Fg'
  when m.topic_id = 4 and m.difficulty = 1 then 'https://www.youtube.com/embed/2lIbLRgnBe8'
  when m.topic_id = 4 and m.difficulty = 2 then 'https://www.youtube.com/embed/s2HCrhNVfak'

  when m.topic_id = 5 and m.difficulty = 0 then 'https://www.youtube.com/embed/sF6AMj3H0jg'
  when m.topic_id = 5 and m.difficulty = 1 then 'https://www.youtube.com/embed/yZdMfc8v5yc'
  when m.topic_id = 5 and m.difficulty = 2 then 'https://www.youtube.com/embed/QKMh2CA1wjc'
  else null
end
from public.learning_modules m
where s.module_id = m.id
  and s."order" = 2;

-- order 3 => secondary image (unique per intermediate/advanced module)
update public.learning_sections s
set image_url = case
  when m.topic_id = 1 and m.difficulty = 1 then 'https://images.unsplash.com/photo-1454165205744-3b78555e5572?w=1200&q=80&auto=format&fit=crop'
  when m.topic_id = 1 and m.difficulty = 2 then 'https://images.unsplash.com/photo-1521540216272-a50305cd4421?w=1200&q=80&auto=format&fit=crop'

  when m.topic_id = 2 and m.difficulty = 1 then 'https://images.unsplash.com/photo-1556742502-ec7c0e9f34b1?w=1200&q=80&auto=format&fit=crop'
  when m.topic_id = 2 and m.difficulty = 2 then 'https://images.unsplash.com/photo-1579621970563-ebec7560ff3e?w=1200&q=80&auto=format&fit=crop'

  when m.topic_id = 3 and m.difficulty = 1 then 'https://images.unsplash.com/photo-1556745757-8d76bdb6984b?w=1200&q=80&auto=format&fit=crop'
  when m.topic_id = 3 and m.difficulty = 2 then 'https://images.unsplash.com/photo-1590283603385-17ffb3a7f29f?w=1200&q=80&auto=format&fit=crop'

  when m.topic_id = 4 and m.difficulty = 1 then 'https://images.unsplash.com/photo-1581092795360-fd1ca04f0952?w=1200&q=80&auto=format&fit=crop'
  when m.topic_id = 4 and m.difficulty = 2 then 'https://images.unsplash.com/photo-1581092335397-9583eb92d232?w=1200&q=80&auto=format&fit=crop'

  when m.topic_id = 5 and m.difficulty = 1 then 'https://images.unsplash.com/photo-1460925895917-afdab827c52f?w=1200&q=80&auto=format&fit=crop'
  when m.topic_id = 5 and m.difficulty = 2 then 'https://images.unsplash.com/photo-1553729459-efe14ef6055d?w=1200&q=80&auto=format&fit=crop'
  else null
end
from public.learning_modules m
join public.learning_topics t on t.id = m.topic_id
where s.module_id = m.id
  and s."order" = 3
  and m.difficulty in (1,2);

-- Optional checks
-- select achievement_id, name, image_url from public.reward_achievement_catalog order by achievement_id;
-- select badge_id, name, image_url from public.community_badge_catalog order by badge_id;
-- select id, title, image_url from public.community_events where image_url is not null order by id;
-- select id, title, image_url from public.community_resources where image_url is not null order by id;
-- select t.title, m.difficulty, s."order", count(*) total,
--        count(s.image_url) image_count, count(s.video_url) video_count
-- from public.learning_sections s
-- join public.learning_modules m on m.id = s.module_id
-- join public.learning_topics t on t.id = m.topic_id
-- group by t.title, m.difficulty, s."order"
-- order by t.title, m.difficulty, s."order";

