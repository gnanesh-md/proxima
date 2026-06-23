// Merge OTP
document.getElementById("otpForm")?.addEventListener("submit", function (e) {
    let otp = "";
    document.querySelectorAll(".otp-box").forEach(box => otp += box.value);
    if (otp.length !== 6) {
        e.preventDefault();
        document.getElementById("otpError").innerText = "Enter 6 digit OTP.";
        return false;
    }
    document.getElementById("otpInput").value = otp;
});

// Password validation
document.getElementById("resetPasswordForm")?.addEventListener("submit", function (e) {
    const pwd = document.getElementById("NewPassword").value;
    const confirm = document.getElementById("ConfirmPassword").value;
    const errorEl = document.getElementById("resetError");

    // Regex for strong password
    const regex = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$/;

    if (!regex.test(pwd)) {
        e.preventDefault();
        errorEl.innerText = "Password must be at least 8 characters, with uppercase, lowercase, number, and special character.";
        return false;
    }
    if (pwd !== confirm) {
        e.preventDefault();
        errorEl.innerText = "Passwords do not match.";
        return false;
    }
});

// OTP helper functions
function isNumberKey(evt) {
    var charCode = evt.which ? evt.which : evt.keyCode;
    return charCode >= 48 && charCode <= 57;
}
function moveToNext(current, index) {
    if (current.value.length === 1) {
        let next = document.querySelectorAll(".otp-box")[index + 1];
        if (next) next.focus();
    }
}
function moveToPrev(event, index) {
    if (event.key === "Backspace" && event.target.value === "") {
        let prev = document.querySelectorAll(".otp-box")[index - 1];
        if (prev) prev.focus();
    }
}

// OTP Countdown Timer (60 seconds)
function startOtpTimer() {
    let expirySeconds = 60;
    const countdownText = document.getElementById("countdownText");
    const resendForm = document.getElementById("resendForm");
    const verifyBtn = document.getElementById("verifyBtn");

    if (!countdownText) return;

    // reset UI
    countdownText.innerText = "";
    resendForm.style.display = "none";
    verifyBtn.disabled = false;

    const timer = setInterval(function () {
        if (expirySeconds <= 0) {
            clearInterval(timer);
            countdownText.innerText = "OTP expired.";
            resendForm.style.display = "block";   // show resend
            verifyBtn.disabled = true;            // disable verify
        } else {
            countdownText.innerText = "Resend OTP in " + expirySeconds + "s";
            expirySeconds--;
        }
    }, 1000);

    // when resend form is submitted → reset timer
    resendForm?.addEventListener("submit", function () {
        clearInterval(timer);
        verifyBtn.disabled = false;
        countdownText.innerText = "";
    });
}

// Call startOtpTimer when OTP panel is displayed
document.addEventListener("DOMContentLoaded", function () {
    if (document.getElementById("otpForm")) {
        startOtpTimer();
    }
});
