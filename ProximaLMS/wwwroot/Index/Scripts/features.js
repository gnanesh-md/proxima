
 const scrollElements = document.querySelectorAll('.scroll-animate');

 const scrollObserver = new IntersectionObserver((entries, observer) => {
 entries.forEach(entry => {
 if (entry.isIntersecting) {
 // Calculate delay based on element's index in the NodeList
 const index = Array.from(scrollElements).indexOf(entry.target);
 const delay = index * 80; // 80ms delay between each element

 setTimeout(() => {
 entry.target.classList.add('visible');
 }, delay);

 observer.unobserve(entry.target); // Stop observing once visible
 }
 });
 }, { threshold: 0.2 }); // Trigger when 20% of the element is visible

 scrollElements.forEach(el => scrollObserver.observe(el));
