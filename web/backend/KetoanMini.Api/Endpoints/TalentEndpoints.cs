using System.Security.Claims;
using System.Text.Json;
using KetoanMini.Api.Data;

namespace KetoanMini.Api.Endpoints;

public static class TalentEndpoints
{
    public static async Task EnsureTables(Database db)
    {
        await using var c = await db.OpenAsync();
        await c.Cmd("""
            CREATE TABLE IF NOT EXISTS hr_onboarding_tasks(
              id uuid PRIMARY KEY, employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
              title varchar(200) NOT NULL, action_key varchar(40) NOT NULL DEFAULT '', due_at timestamptz NULL,
              policy_text text NOT NULL DEFAULT '', completed_at timestamptz NULL, acknowledged_at timestamptz NULL,
              created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);
            CREATE INDEX IF NOT EXISTS ix_onboarding_employee ON hr_onboarding_tasks(employee_id,due_at);

            CREATE TABLE IF NOT EXISTS hr_performance_goals(
              id uuid PRIMARY KEY, employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
              title varchar(200) NOT NULL, description text NOT NULL DEFAULT '', target numeric(12,2) NOT NULL DEFAULT 100,
              progress numeric(12,2) NOT NULL DEFAULT 0, unit varchar(30) NOT NULL DEFAULT '%', due_at timestamptz NULL,
              updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);
            CREATE TABLE IF NOT EXISTS hr_performance_reviews(
              id uuid PRIMARY KEY, employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
              period varchar(30) NOT NULL, closes_at timestamptz NULL, self_assessment text NOT NULL DEFAULT '',
              manager_comment text NOT NULL DEFAULT '', score numeric(5,2) NULL, status varchar(20) NOT NULL DEFAULT 'open',
              updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);

            CREATE TABLE IF NOT EXISTS hr_training_courses(
              id uuid PRIMARY KEY, title varchar(200) NOT NULL, description text NOT NULL DEFAULT '',
              material_url text NOT NULL DEFAULT '', video_url text NOT NULL DEFAULT '', quiz jsonb NOT NULL DEFAULT '[]',
              certificate_valid_months integer NULL, active boolean NOT NULL DEFAULT TRUE, created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);
            CREATE TABLE IF NOT EXISTS hr_training_enrollments(
              course_id uuid NOT NULL REFERENCES hr_training_courses(id) ON DELETE CASCADE,
              employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
              progress integer NOT NULL DEFAULT 0, resume_seconds integer NOT NULL DEFAULT 0, score numeric(5,2) NULL,
              completed_at timestamptz NULL, certificate_expires_at date NULL, updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
              PRIMARY KEY(course_id,employee_id));
            CREATE TABLE IF NOT EXISTS hr_employee_benefits(
              id uuid PRIMARY KEY,employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
              benefit_type varchar(40) NOT NULL,title varchar(200) NOT NULL,value_text text NOT NULL DEFAULT '',
              valid_from date NULL,valid_to date NULL,created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);
            CREATE TABLE IF NOT EXISTS hr_employee_rewards(
              id uuid PRIMARY KEY,employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
              title varchar(200) NOT NULL,points integer NOT NULL DEFAULT 0,awarded_at date NOT NULL DEFAULT CURRENT_DATE,note text NOT NULL DEFAULT '');
            """).ExecuteNonQueryAsync();
    }

    public static void MapTalent(this WebApplication app)
    {
        var g = app.MapGroup("/api/talent").RequireAuthorization();

        g.MapGet("/onboarding", async (ClaimsPrincipal u, Database db) =>
        {
            await using var c = await db.OpenAsync(); var emp = await HrEndpoints.EnsureEmployeeForUser(c,u.Username());
            var mentor = await c.Cmd("SELECT COALESCE(m.full_name,'') FROM hr_employees e LEFT JOIN hr_employees m ON m.id=e.manager_id WHERE e.id=@e")
                .With("@e",emp).ExecuteScalarAsync() as string ?? "";
            var items = new List<object>();
            await using var r = await c.Cmd("SELECT id,title,action_key,due_at,policy_text,completed_at,acknowledged_at FROM hr_onboarding_tasks WHERE employee_id=@e ORDER BY completed_at NULLS FIRST,due_at NULLS LAST")
                .With("@e",emp).ExecuteReaderAsync();
            while(await r.ReadAsync()) items.Add(new {id=r.Guid("id"),title=r.Str("title"),actionKey=r.Str("action_key"),dueAt=r.DtNull("due_at"),policyText=r.Str("policy_text"),completed=r.DtNull("completed_at")!=null,acknowledged=r.DtNull("acknowledged_at")!=null});
            return Results.Ok(new {mentorName=mentor,items});
        });
        g.MapPost("/onboarding/{id:guid}/complete", async(Guid id, ClaimsPrincipal u, Database db) => {
            await using var c=await db.OpenAsync(); var emp=await HrEndpoints.EnsureEmployeeForUser(c,u.Username());
            var n=await c.Cmd("UPDATE hr_onboarding_tasks SET completed_at=CURRENT_TIMESTAMP,acknowledged_at=CASE WHEN policy_text<>'' THEN CURRENT_TIMESTAMP ELSE acknowledged_at END WHERE id=@id AND employee_id=@e")
                .With("@id",id).With("@e",emp).ExecuteNonQueryAsync(); return n==0?Results.NotFound():Results.NoContent(); });

        g.MapGet("/performance", async(ClaimsPrincipal u, Database db) => {
            await using var c=await db.OpenAsync(); var emp=await HrEndpoints.EnsureEmployeeForUser(c,u.Username());
            var goals=new List<object>(); await using(var r=await c.Cmd("SELECT id,title,description,target,progress,unit,due_at FROM hr_performance_goals WHERE employee_id=@e ORDER BY due_at NULLS LAST").With("@e",emp).ExecuteReaderAsync())
                while(await r.ReadAsync()) goals.Add(new{id=r.Guid("id"),title=r.Str("title"),description=r.Str("description"),target=r.Dec("target"),progress=r.Dec("progress"),unit=r.Str("unit"),dueAt=r.DtNull("due_at")});
            var reviews=new List<object>(); await using(var r=await c.Cmd("SELECT id,period,closes_at,self_assessment,manager_comment,score,status FROM hr_performance_reviews WHERE employee_id=@e ORDER BY period DESC").With("@e",emp).ExecuteReaderAsync())
                while(await r.ReadAsync()) reviews.Add(new{id=r.Guid("id"),period=r.Str("period"),closesAt=r.DtNull("closes_at"),selfAssessment=r.Str("self_assessment"),managerComment=r.Str("manager_comment"),score=r.IsDBNull(r.GetOrdinal("score"))?(decimal?)null:r.Dec("score"),status=r.Str("status")});
            return Results.Ok(new{goals,reviews}); });
        g.MapPut("/performance/goals/{id:guid}", async(Guid id, ProgressReq req, ClaimsPrincipal u, Database db)=>{
            await using var c=await db.OpenAsync();var emp=await HrEndpoints.EnsureEmployeeForUser(c,u.Username()); var n=await c.Cmd("UPDATE hr_performance_goals SET progress=LEAST(target,GREATEST(0,@p)),updated_at=CURRENT_TIMESTAMP WHERE id=@id AND employee_id=@e")
                .With("@p",req.Progress).With("@id",id).With("@e",emp).ExecuteNonQueryAsync();return n==0?Results.NotFound():Results.NoContent();});
        g.MapPut("/performance/reviews/{id:guid}/self", async(Guid id, SelfReviewReq req, ClaimsPrincipal u, Database db)=>{
            await using var c=await db.OpenAsync();var emp=await HrEndpoints.EnsureEmployeeForUser(c,u.Username());var n=await c.Cmd("UPDATE hr_performance_reviews SET self_assessment=@v,updated_at=CURRENT_TIMESTAMP WHERE id=@id AND employee_id=@e AND status='open'")
                .With("@v",req.Text??"").With("@id",id).With("@e",emp).ExecuteNonQueryAsync();return n==0?Results.BadRequest(new{message="Kỳ đánh giá đã đóng."}):Results.NoContent();});

        g.MapGet("/training", async(ClaimsPrincipal u, Database db)=>{
            await using var c=await db.OpenAsync();var emp=await HrEndpoints.EnsureEmployeeForUser(c,u.Username());var list=new List<object>();
            await using var r=await c.Cmd("""
              SELECT c.id,c.title,c.description,c.material_url,c.video_url,c.quiz::text quiz,
                     COALESCE(e.progress,0) progress,COALESCE(e.resume_seconds,0) resume_seconds,e.score,e.completed_at,e.certificate_expires_at
              FROM hr_training_courses c LEFT JOIN hr_training_enrollments e ON e.course_id=c.id AND e.employee_id=@e WHERE c.active=TRUE ORDER BY c.created_at DESC
              """).With("@e",emp).ExecuteReaderAsync();
            while(await r.ReadAsync()) list.Add(new{id=r.Guid("id"),title=r.Str("title"),description=r.Str("description"),materialUrl=r.Str("material_url"),videoUrl=r.Str("video_url"),quiz=PublicQuiz(r.Str("quiz")),progress=r.Int("progress"),resumeSeconds=r.Int("resume_seconds"),score=r.IsDBNull(r.GetOrdinal("score"))?(decimal?)null:r.Dec("score"),completedAt=r.DtNull("completed_at"),certificateExpiresAt=DateOrNull(r,"certificate_expires_at")});
            return Results.Ok(list);});
        g.MapPut("/training/{id:guid}/progress", async(Guid id, TrainingProgressReq req, ClaimsPrincipal u, Database db)=>{
            await using var c=await db.OpenAsync();var emp=await HrEndpoints.EnsureEmployeeForUser(c,u.Username());
            await c.Cmd("INSERT INTO hr_training_enrollments(course_id,employee_id,progress,resume_seconds) VALUES(@c,@e,@p,@s) ON CONFLICT(course_id,employee_id) DO UPDATE SET progress=GREATEST(hr_training_enrollments.progress,@p),resume_seconds=@s,updated_at=CURRENT_TIMESTAMP")
                .With("@c",id).With("@e",emp).With("@p",Math.Clamp(req.Progress,0,100)).With("@s",Math.Max(0,req.ResumeSeconds)).ExecuteNonQueryAsync();return Results.NoContent();});
        g.MapPost("/training/{id:guid}/quiz", async(Guid id, QuizReq req, ClaimsPrincipal u, Database db)=>{
            await using var c=await db.OpenAsync();var emp=await HrEndpoints.EnsureEmployeeForUser(c,u.Username());
            var raw=await c.Cmd("SELECT quiz::text FROM hr_training_courses WHERE id=@id AND active=TRUE").With("@id",id).ExecuteScalarAsync() as string;
            if(raw is null)return Results.NotFound(); using var doc=JsonDocument.Parse(raw);var questions=doc.RootElement.EnumerateArray().ToList();
            var correct=0;for(var i=0;i<questions.Count;i++){var expected=questions[i].TryGetProperty("correct",out var x)?x.GetString()??"":"";if(i<req.Answers.Count&&req.Answers[i]==expected)correct++;}
            var score=questions.Count==0?100m:Math.Round(correct*100m/questions.Count,2);var passed=score>=70;
            await c.Cmd("""
              INSERT INTO hr_training_enrollments(course_id,employee_id,progress,score,completed_at)
              VALUES(@c,@e,@p,@s,CASE WHEN @pass THEN CURRENT_TIMESTAMP ELSE NULL END)
              ON CONFLICT(course_id,employee_id) DO UPDATE SET progress=GREATEST(hr_training_enrollments.progress,@p),score=@s,
              completed_at=CASE WHEN @pass THEN COALESCE(hr_training_enrollments.completed_at,CURRENT_TIMESTAMP) ELSE hr_training_enrollments.completed_at END,updated_at=CURRENT_TIMESTAMP
              """).With("@c",id).With("@e",emp).With("@p",passed?100:0).With("@s",score).With("@pass",passed).ExecuteNonQueryAsync();
            return Results.Ok(new{score,passed});});
        g.MapGet("/benefits",async(ClaimsPrincipal u,Database db)=>{
            await using var c=await db.OpenAsync();var emp=await HrEndpoints.EnsureEmployeeForUser(c,u.Username());
            decimal total=0,used=0;await using(var r=await c.Cmd("SELECT COALESCE(SUM(total_days),0) total,COALESCE(SUM(used_days),0) used FROM hr_leave_balances WHERE employee_id=@e AND year=EXTRACT(YEAR FROM CURRENT_DATE)").With("@e",emp).ExecuteReaderAsync()){if(await r.ReadAsync()){total=r.Dec("total");used=r.Dec("used");}}
            var leaveHistory=new List<object>();await using(var r=await c.Cmd("SELECT request_no,payload::text payload,status,created_at FROM hr_requests WHERE employee_id=@e AND req_type IN('leave','sick') ORDER BY created_at DESC LIMIT 30").With("@e",emp).ExecuteReaderAsync())while(await r.ReadAsync())leaveHistory.Add(new{requestNo=r.Str("request_no"),payload=Parse(r.Str("payload")),status=r.Str("status"),createdAt=r.Dt("created_at")});
            var benefits=new List<object>();await using(var r=await c.Cmd("SELECT id,benefit_type,title,value_text,valid_from,valid_to FROM hr_employee_benefits WHERE employee_id=@e ORDER BY valid_to NULLS LAST").With("@e",emp).ExecuteReaderAsync())while(await r.ReadAsync())benefits.Add(new{id=r.Guid("id"),type=r.Str("benefit_type"),title=r.Str("title"),value=r.Str("value_text"),validFrom=DateOrNull(r,"valid_from"),validTo=DateOrNull(r,"valid_to")});
            var rewards=new List<object>();await using(var r=await c.Cmd("SELECT id,title,points,awarded_at,note FROM hr_employee_rewards WHERE employee_id=@e ORDER BY awarded_at DESC").With("@e",emp).ExecuteReaderAsync())while(await r.ReadAsync())rewards.Add(new{id=r.Guid("id"),title=r.Str("title"),points=r.Int("points"),awardedAt=r.DateOnly("awarded_at"),note=r.Str("note")});
            await using var er=await c.Cmd("SELECT dob,hire_date FROM hr_employees WHERE id=@e").With("@e",emp).ExecuteReaderAsync();DateOnly? dob=null,hire=null;if(await er.ReadAsync()){dob=DateOrNull(er,"dob");hire=DateOrNull(er,"hire_date");}
            return Results.Ok(new{leaveTotal=total,leaveUsed=used,leaveRemaining=total-used,leaveHistory,benefits,rewards,birthday=dob,hireDate=hire});});
    }

    static JsonElement Parse(string s){try{return JsonDocument.Parse(s).RootElement.Clone();}catch{return JsonDocument.Parse("[]").RootElement.Clone();}}
    static List<object> PublicQuiz(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.EnumerateArray().Select(q => (object)new
            {
                text = q.TryGetProperty("text", out var text) ? text.GetString() ?? "" : "",
                options = q.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array
                    ? options.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                    : new List<string>()
            }).ToList();
        }
        catch { return new List<object>(); }
    }
    static DateOnly? DateOrNull(Npgsql.NpgsqlDataReader r,string c)=>r.IsDBNull(r.GetOrdinal(c))?null:r.DateOnly(c);
    public record ProgressReq(decimal Progress); public record SelfReviewReq(string? Text); public record TrainingProgressReq(int Progress,int ResumeSeconds); public record QuizReq(List<string> Answers);
}
