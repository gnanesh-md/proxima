
 function validatePasswords() {
 const pass = document.getElementById("password").value;
 const confirm = document.getElementById("confirmPassword").value;
 const msg = document.getElementById("passwordMessage");
 if (pass.length < 8 || !/[0-9]/.test(pass) || !/[A-Z]/.test(pass)) {
 msg.textContent = "Password must be at least 8 characters long, include a number and an uppercase letter.";
 msg.style.color = 'red';
 return false;
 } else {
 msg.textContent = "";
 }
 if (pass !== confirm) {
 msg.textContent = "Passwords do not match.";
 msg.style.color = 'red';
 return false;
 } else {
 msg.textContent = "";
 }
 alert("Registration Successful!");
 return true;
 }
 function handleDemoForm(e) {
 e.preventDefault();
 const popup = document.getElementById("successPopup");
 popup.style.display = "block";
 setTimeout(() => popup.style.display = "none", 4000);
 const title = "Proxima LMS Demo";
 const description = "A demo session requested via the Proxima LMS form.";
 const location = "Online";
 const start = new Date().toISOString().replace(/-|:|\.\d+/g, "");
 const end = new Date(Date.now() + 3600000).toISOString().replace(/-|:|\.\d+/g, "");
 const calendarUrl = `https://calendar.google.com/calendar/render?action=TEMPLATE&text=${encodeURIComponent(title)}&details=${encodeURIComponent(description)}&location=${encodeURIComponent(location)}&dates=${start}/${end}`;
window.open(calendarUrl, '_blank');
 return false;
 }
