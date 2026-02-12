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
values ('Admin', 1200)
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
  author_reputation int not null default 0,
  excerpt text,
  tags text,
  upvotes int not null default 0,
  view_count int not null default 0,
  created_at timestamptz not null default now()
);

alter table public.community_threads
  drop column if exists is_solved;

create table if not exists public.community_thread_replies (
  id bigint primary key,
  thread_id bigint not null references public.community_threads(id) on delete cascade,
  author text not null,
  message text not null,
  created_at timestamptz not null default now()
);

create table if not exists public.community_thread_votes (
  id bigint primary key,
  thread_id bigint not null references public.community_threads(id) on delete cascade,
  user_id text not null,
  created_at timestamptz not null default now(),
  unique (thread_id, user_id)
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

-- =========================
-- SEED: MEDIA (images/videos)
-- =========================
update public.learning_sections set
  image_url = 'https://images.unsplash.com/photo-1520607162513-77705c0f0d4a?w=1200&q=80&auto=format&fit=crop'
where id = 1001;

update public.learning_sections set
  video_url = 'https://www.youtube.com/embed/8Z0UWv8t-mE'
where id = 1002;

update public.learning_sections set
  image_url = 'https://images.unsplash.com/photo-1454165205744-3b78555e5572?w=1200&q=80&auto=format&fit=crop'
where id = 3101;

update public.learning_sections set
  video_url = 'https://www.youtube.com/embed/G3K6Y3-0Hqk'
where id = 5101;

update public.learning_sections set
  image_url = 'https://images.unsplash.com/photo-1521791136064-7986c2920216?w=1200&q=80&auto=format&fit=crop'
where id = 4201;

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
(102008, 102, 'What is the best way to prevent “category leakage”?'),
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
(103003, 103, 'A 3–6 month cash buffer is mainly used to handle:'),
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
(20200801, 202008, 'Balance ÷ credit limit', true),
(20200802, 202008, 'Income ÷ rent', false),
(20200803, 202008, 'Loan term ÷ interest rate', false),
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
(403007, 403, 'Which is a lean “waste” example?'),
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
(50100701, 501007, 'Price × quantity sold', true),
(50100702, 501007, 'Profit ÷ tax', false),
(50100703, 501007, 'Costs − revenue', false),
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
