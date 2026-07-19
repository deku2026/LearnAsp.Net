const session = document.querySelector("#session");
const login = document.querySelector("#login");
const logout = document.querySelector("#logout");
const workspace = document.querySelector("#workspace");
const form = document.querySelector("#course-form");
const message = document.querySelector("#message");
const courses = document.querySelector("#courses");

const bffFetch = (url, options = {}) => fetch(url, {
  ...options,
  credentials: "same-origin",
  headers: {
    "X-CSRF": "1",
    ...(options.headers || {}),
  },
});

async function loadSession() {
  const response = await fetch("/bff/user", { credentials: "same-origin" });
  const user = await response.json();
  if (!user.isAuthenticated) {
    session.textContent = "未登录";
    return;
  }

  session.textContent = user.name || user.subject;
  login.hidden = true;
  logout.hidden = false;
  workspace.hidden = false;
  await loadCourses();
}

async function loadCourses() {
  const response = await bffFetch("/bff/api/courses");
  if (!response.ok) {
    message.textContent = `加载失败 (${response.status})`;
    return;
  }

  const data = await response.json();
  courses.replaceChildren(...data.map(course => {
    const item = document.createElement("li");
    item.textContent = `${course.code} · ${course.title}`;
    return item;
  }));
}

form.addEventListener("submit", async event => {
  event.preventDefault();
  const response = await bffFetch("/bff/api/courses", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      code: document.querySelector("#code").value,
      title: document.querySelector("#title").value,
    }),
  });
  message.textContent = response.ok ? "创建成功，token 始终留在 BFF。" : `创建失败 (${response.status})`;
  if (response.ok) {
    await loadCourses();
  }
});

logout.addEventListener("click", async () => {
  const response = await bffFetch("/bff/logout", { method: "POST" });
  if (response.ok) {
    window.location.assign("/");
  } else {
    message.textContent = `退出失败 (${response.status})`;
  }
});

loadSession().catch(() => {
  session.textContent = "会话服务暂不可用";
});
