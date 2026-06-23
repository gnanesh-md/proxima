const form = document.querySelector('form');

// Create and style an improved loading overlay
const loadingOverlay = document.createElement('div');
loadingOverlay.style.position = 'fixed';
loadingOverlay.style.top = 0;
loadingOverlay.style.left = 0;
loadingOverlay.style.width = '100vw';
loadingOverlay.style.height = '100vh';
loadingOverlay.style.backdropFilter = 'blur(4px)';
loadingOverlay.style.backgroundColor = 'rgba(0, 0, 0, 0.5)';
loadingOverlay.style.display = 'flex';
loadingOverlay.style.flexDirection = 'column';
loadingOverlay.style.alignItems = 'center';
loadingOverlay.style.justifyContent = 'center';
loadingOverlay.style.zIndex = 9999;
loadingOverlay.style.display = 'none';

// Spinner container
const spinnerContainer = document.createElement('div');
spinnerContainer.style.width = '100px';
spinnerContainer.style.height = '100px';
spinnerContainer.style.border = '12px solid #f3f3f3';
spinnerContainer.style.borderTop = '12px solid #800000';
spinnerContainer.style.borderRadius = '50%';
spinnerContainer.style.animation = 'spin 1s ease-in-out infinite';

// Loading text
const loadingText = document.createElement('div');
loadingText.innerText = 'Sending your message...';
loadingText.style.marginTop = '30px';
loadingText.style.fontSize = '1.5rem';
loadingText.style.color = '#fff';
loadingText.style.fontWeight = '500';
loadingText.style.fontFamily = 'Poppins, sans-serif';
loadingText.style.textAlign = 'center';
loadingText.style.maxWidth = '80%';

// Append everything
loadingOverlay.appendChild(spinnerContainer);
loadingOverlay.appendChild(loadingText);
document.body.appendChild(loadingOverlay);

// Spinner animation CSS
const styleSheet = document.createElement('style');
styleSheet.type = 'text/css';
styleSheet.innerText = `
@keyframes spin {
  0% { transform: rotate(0deg) scale(1); }
  50% { transform: rotate(180deg) scale(1.1); }
  100% { transform: rotate(360deg) scale(1); }
}`;
document.head.appendChild(styleSheet);


form.addEventListener('submit', async (e) => {
  e.preventDefault();
  loadingOverlay.style.display = 'flex'; // Show loading

  const data = {
    name: form.name.value,
    phone: form.phone.value,
    email: form.email.value,
    subject: form.subject.value,
    message: form.message.value
  };

  try {
    const res = await fetch('http://localhost:5000/contact', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });

    const result = await res.json();

    if (result.success) {
      form.reset();
      window.location.href = 'thank-you.html'; // 🔁 Redirect on success
    } else {
      alert('❌ Something went wrong. Please try again later.');
    }
  } catch (err) {
    alert('❌ Failed to send message.');
  } finally {
    loadingOverlay.style.display = 'none'; // Hide loading in any case
  }
});
